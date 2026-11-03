// pt-audio.js — the app's sound cues, synthesised with Web Audio.
//
// WHY SYNTHESIS AND NOT FILES: four cues as audio files are four more requests, four
// more things for the service worker to cache, and a fixed pitch. The check-now cue
// has to change pitch with the result — a commute running long sounds higher and
// tenser than one running clear — which a recording cannot do and an oscillator does
// for free. Each cue is two or three enveloped oscillators; the whole synth is a few
// hundred bytes of state.
//
// AUTOPLAY: an AudioContext created outside a user gesture starts suspended, and
// several browsers log a warning for it. So no context exists until the first press
// (pt-fx.js runs the gesture gate and calls `unlock` from inside the event), and no
// cue plays before then — a check result that lands before anyone has touched the page
// is silent rather than a surprise.
//
// Loaded on first use from pt-fx.js `cue()`, which has already checked the mute
// preference; nothing here imports .NET or touches the DOM.

import * as fx from "./pt-fx.js";

/** @type {AudioContext|null} */
let ctx = null;
/** @type {GainNode|null} */
let master = null;

/** Overall level. Cues are confirmation, not music: quiet enough to sit under speech. */
const MASTER_GAIN = 0.16;

function create() {
    const Ctor = window.AudioContext || window.webkitAudioContext;
    if (!Ctor) return null;
    try {
        ctx = new Ctor({ latencyHint: "interactive" });
        master = ctx.createGain();
        master.gain.value = MASTER_GAIN;
        master.connect(ctx.destination);
    } catch {
        ctx = null;
        master = null;
    }
    return ctx;
}

/**
 * Called by the gesture gate, inside the press itself. Creating or resuming here is
 * what makes the context "allowed to start" under every autoplay policy.
 */
function unlock() {
    if (!fx.soundOn()) return;
    if (!ctx) create();
    if (ctx?.state === "suspended") ctx.resume().catch(() => { });
}
fx.onUserGesture(unlock);

// A hidden tab holds a live audio thread open for nothing; suspend it. The next cue
// (or press) resumes it.
document.addEventListener("visibilitychange", () => {
    if (document.hidden && ctx?.state === "running") ctx.suspend().catch(() => { });
});

/**
 * One enveloped note. A fast attack and an exponential release read as a struck
 * chime rather than a beep; `glide` slides the pitch over the note for the save/undo
 * swoops.
 */
function note(freq, start, duration, { type = "sine", gain = 1, glide = null } = {}) {
    const osc = ctx.createOscillator();
    const env = ctx.createGain();
    osc.type = type;
    osc.frequency.setValueAtTime(freq, start);
    if (glide) osc.frequency.exponentialRampToValueAtTime(glide, start + duration * 0.8);

    env.gain.setValueAtTime(0.0001, start);
    env.gain.exponentialRampToValueAtTime(gain, start + 0.012);
    env.gain.exponentialRampToValueAtTime(0.0001, start + duration);

    osc.connect(env).connect(master);
    osc.start(start);
    osc.stop(start + duration + 0.02);
    // Disconnect once finished so a long session does not accumulate dead nodes.
    osc.onended = () => { osc.disconnect(); env.disconnect(); };
}

/** C5. The check cue's pitch for a result exactly on its baseline. */
const CHECK_BASE_HZ = 523.25;

/**
 * Semitones above (or below) the base for a duration ÷ baseline ratio. 10% over the
 * baseline is about three semitones — audible as "higher" without a reference — and
 * the range is clamped so a pathological ratio cannot shriek.
 */
function semitonesFor(ratio) {
    if (!Number.isFinite(ratio) || ratio <= 0) return 0;
    return Math.max(-7, Math.min(12, (ratio - 1) * 30));
}

const CUES = {
    /** Rising two-note chime. Pitch tracks the delay; a late commute ends on a tense minor third. */
    check(t, ratio) {
        const f = CHECK_BASE_HZ * Math.pow(2, semitonesFor(ratio) / 12);
        const late = Number.isFinite(ratio) && ratio > 1.1;
        note(f, t, 0.22, { type: "triangle", gain: 0.9 });
        note(f * (late ? 1.189 : 1.498), t + 0.09, 0.32, { type: "sine", gain: 0.7 });
    },
    /** Descending two-tone bell. Distinct from every other cue so it is recognised unseen. */
    alert(t) {
        note(880, t, 0.35, { type: "triangle", gain: 0.85 });
        note(659.25, t + 0.16, 0.5, { type: "triangle", gain: 0.75 });
    },
    /** A short upward swoop: "done". */
    save(t) {
        note(587.33, t, 0.2, { type: "sine", gain: 0.8, glide: 880 });
    },
    /** The same swoop reversed: "taken back". */
    undo(t) {
        note(698.46, t, 0.22, { type: "sine", gain: 0.75, glide: 415.3 });
    },
};

/**
 * Plays a cue by name. Silent — never throwing — when there has been no gesture yet,
 * the tab is hidden, Web Audio is missing, or the name is unknown.
 *
 * @param {"check"|"alert"|"save"|"undo"} name
 * @param {number|null} ratio  check only: result ÷ baseline, null when no baseline exists
 */
export function cue(name, ratio = null) {
    const play = CUES[name];
    if (!play || document.hidden || !fx.soundOn() || !fx.hasGesture()) return;

    if (!ctx && !create()) return;
    if (ctx.state === "suspended") ctx.resume().catch(() => { });

    // A few ms of lead so the first envelope point is never already in the past.
    play(ctx.currentTime + 0.01, ratio);
}

// pt-fx.js — the shared runtime the chart and the map animate on.
//
// WHY THIS EXISTS: every effect calling requestAnimationFrame on its own means several
// callbacks per frame, several ideas about whether the tab is visible, and several places
// to remember prefers-reduced-motion. Centralising it decides "are we animating, and how
// hard" exactly once per frame.
//
// Two things live here:
//   1. ONE ticker. Effects register a step(dt, now) function and get called from a
//      single rAF loop that stops itself the moment nothing is registered. One-off
//      redraws (a re-measure) go through `nextFrame` on the same loop.
//   2. THE MOTION LEVEL. "full" | "reduced" | "off", from the user's own setting,
//      with the OS prefers-reduced-motion preference as the floor.
//
// .NET (FxService) talks to exactly this one JS surface, so every effect inherits the
// same visibility and reduced-motion rules.

const STORAGE_MOTION = "pt-motion-level";

/** Frame budget guard. Anything slower than this and we shed work rather than stutter. */
const SLOW_FRAME_MS = 24;          // ~40fps — two consecutive misses trigger a downgrade
const SLOW_FRAMES_BEFORE_DEGRADE = 30;

const effects = new Map();
let rafId = null;
let lastTime = 0;
let slowFrames = 0;

/** Set when sustained frame times force a quality drop; effects read it to shed detail. */
let degraded = false;

// ── Preferences ──────────────────────────────────────────────────────────────

function read(key, fallback) {
    try {
        const v = localStorage.getItem(key);
        return v === null ? fallback : v;
    } catch {
        return fallback;   // private mode
    }
}

function write(key, value) {
    try { localStorage.setItem(key, value); } catch { /* private mode */ }
}

const reducedMedia = window.matchMedia("(prefers-reduced-motion: reduce)");

/**
 * The effective motion level.
 *
 * The OS preference is a FLOOR, not a default: someone who has asked their system
 * for reduced motion has asked every app, and an in-app "full" setting must not
 * override that. They can still choose "off" — going quieter is always allowed.
 */
export function motionLevel() {
    const chosen = read(STORAGE_MOTION, "full");
    if (chosen === "off") return "off";
    if (reducedMedia.matches) return chosen === "full" ? "reduced" : chosen;
    return chosen;
}

export function setMotionLevel(level) {
    write(STORAGE_MOTION, level);
    document.documentElement.setAttribute("data-motion", motionLevel());
}

/** True when decorative animation should run at all. */
export function animates() {
    return motionLevel() === "full";
}

reducedMedia.addEventListener("change", () => {
    document.documentElement.setAttribute("data-motion", motionLevel());
});

// Stamped on <html> so CSS can react without asking JS anything.
document.documentElement.setAttribute("data-motion", motionLevel());

// ── The ticker ───────────────────────────────────────────────────────────────

function frame(now) {
    rafId = null;

    const dt = lastTime ? Math.min((now - lastTime) / 1000, 0.1) : 0.016;
    lastTime = now;

    // Sustained slow frames mean this device cannot afford what we are asking for.
    // Rather than stutter, tell effects to shed detail — they read `isDegraded()`.
    if (dt * 1000 > SLOW_FRAME_MS) {
        if (++slowFrames >= SLOW_FRAMES_BEFORE_DEGRADE) degraded = true;
    } else if (slowFrames > 0) {
        slowFrames--;
    }

    for (const [, effect] of effects) {
        // One effect throwing must not stop every other effect on the page.
        try {
            if (effect.fps) {
                effect.acc += dt;
                const period = 1 / effect.fps;
                if (effect.acc < period) continue;
                effect.step(effect.acc, now);
                effect.acc = 0;
            } else {
                effect.step(dt, now);
            }
        } catch {
            effects.delete(effect.key);
        }
    }

    if (effects.size > 0) schedule();
    else lastTime = 0;
}

function schedule() {
    if (rafId === null && !document.hidden) rafId = requestAnimationFrame(frame);
}

/**
 * Registers a per-frame callback. Returns an unregister function.
 *
 * @param {string} key      unique id; registering the same key twice replaces the first
 * @param {(dt:number, now:number)=>void} step
 * @param {{fps?: number}} [options]  cap this effect's rate — a slow background wash
 *                                    at 30fps looks identical and costs half as much
 */
export function register(key, step, options = {}) {
    effects.set(key, { key, step, fps: options.fps ?? 0, acc: 0 });
    lastTime = 0;
    schedule();
    return () => effects.delete(key);
}

export function isDegraded() {
    return degraded;
}

/**
 * Runs `fn` once on the next shared frame. For redraws that are not animations — a
 * drag on the terrain, a map re-measure — so they still coalesce with whatever else
 * is drawing this frame instead of queueing a second rAF of their own. Calling it
 * again with the same key before the frame lands keeps only the latest `fn`.
 */
export function nextFrame(key, fn) {
    const stop = register(`once:${key}`, () => { stop(); fn(); });
    return stop;
}

// A hidden tab must not burn a frame budget. rAF already throttles hard in most
// browsers, but a backgrounded PWA on a phone can keep ticking — this stops it dead.
document.addEventListener("visibilitychange", () => {
    if (document.hidden) {
        if (rafId !== null) cancelAnimationFrame(rafId);
        rafId = null;
        lastTime = 0;
    } else {
        schedule();
    }
});

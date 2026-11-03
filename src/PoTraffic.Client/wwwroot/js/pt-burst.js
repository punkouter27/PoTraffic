// pt-burst.js — the particle burst for a check that came back faster than usual.
//
// WHY: "faster than baseline" is the one piece of good news the app delivers, and a
// toast that says "32 min" reads the same whether 32 is good or bad. A short burst out
// of the button that was pressed says "good" before the number is read. It fires only
// for that case, so it stays a signal rather than becoming confetti on every click.
//
// One viewport-sized canvas2D overlay, created for the burst and removed the moment
// the last particle fades — nothing is left on the page between bursts. It runs on the
// shared ticker in pt-fx.js, and pt-fx only loads this module when motion is "full",
// so reduced motion never sees it. Canvas2D rather than WebGL: forty dots for under a
// second is far below the point where a GPU context pays for its own setup.

import * as fx from "./pt-fx.js";

const COUNT_FULL = 42;
const COUNT_DEGRADED = 18;

/** Seconds a particle lives. Short: the burst is punctuation. */
const LIFE = 0.9;
const GRAVITY = 900;   // px/s², enough to arc rather than drift

let canvas = null;
let ctx = null;
let particles = [];
let stop = null;
let palette = null;

function ensureCanvas() {
    if (canvas?.isConnected) return true;
    canvas = document.createElement("canvas");
    canvas.className = "pt-burst-canvas";
    canvas.setAttribute("aria-hidden", "true");
    document.body.appendChild(canvas);
    ctx = canvas.getContext("2d");
    if (!ctx) { canvas.remove(); canvas = null; return false; }
    return true;
}

function resize() {
    const scale = fx.dpr();
    const w = window.innerWidth, h = window.innerHeight;
    if (canvas.width !== Math.round(w * scale) || canvas.height !== Math.round(h * scale)) {
        canvas.width = Math.round(w * scale);
        canvas.height = Math.round(h * scale);
    }
    ctx.setTransform(scale, 0, 0, scale, 0, 0);
}

/** Success green with a brand-blue and white minority, resolved from the live theme. */
function colours() {
    const toCss = ([r, g, b]) => `${Math.round(r * 255)},${Math.round(g * 255)},${Math.round(b * 255)}`;
    return [
        toCss(fx.resolveRgb(document.body, "var(--pt-traffic-clear)", [0.09, 0.5, 0.24])),
        toCss(fx.resolveRgb(document.body, "var(--pt-traffic-clear)", [0.09, 0.5, 0.24])),
        toCss(fx.resolveRgb(document.body, "var(--pt-color-brand-400)", [0.38, 0.65, 0.98])),
        "255,255,255",
    ];
}

function step(dt) {
    if (!canvas?.isConnected) { finish(); return; }
    resize();
    ctx.clearRect(0, 0, canvas.width, canvas.height);

    let alive = 0;
    for (const p of particles) {
        p.age += dt;
        if (p.age >= p.life) continue;
        alive++;
        p.vx *= 1 - 1.6 * dt;              // air drag, so the ring slows into an arc
        p.vy = p.vy * (1 - 1.6 * dt) + GRAVITY * dt;
        p.x += p.vx * dt;
        p.y += p.vy * dt;

        const k = 1 - p.age / p.life;
        ctx.globalAlpha = k * k;
        ctx.fillStyle = `rgb(${p.colour})`;
        ctx.beginPath();
        ctx.arc(p.x, p.y, p.size * (0.6 + 0.4 * k), 0, Math.PI * 2);
        ctx.fill();
    }
    ctx.globalAlpha = 1;

    if (alive === 0) finish();
}

function finish() {
    stop?.();
    stop = null;
    particles = [];
    canvas?.remove();
    canvas = null;
    ctx = null;
}

/**
 * Bursts at (x, y) in viewport px. A second burst while one is running adds to it
 * rather than restarting, so a double check does not visibly cut the first one off.
 */
export function burst(x, y, kind = "faster") {
    if (!fx.animates() || kind !== "faster") return;
    if (!ensureCanvas()) return;

    // Resolved per burst rather than cached forever: the theme can flip between two.
    if (!stop) palette = colours();
    const count = fx.isDegraded() ? COUNT_DEGRADED : COUNT_FULL;
    for (let i = 0; i < count; i++) {
        const angle = (i / count) * Math.PI * 2 + Math.random() * 0.4;
        // Biased upward: a burst that mostly falls reads as something breaking.
        const speed = 220 + Math.random() * 260;
        particles.push({
            x, y,
            vx: Math.cos(angle) * speed,
            vy: Math.sin(angle) * speed - 240,
            size: 2 + Math.random() * 2.5,
            life: LIFE * (0.7 + Math.random() * 0.5),
            age: 0,
            colour: palette[i % palette.length],
        });
    }

    // Wrapped so a throw tears the overlay down here, instead of the ticker silently
    // dropping the effect and leaving `stop` pointing at a registration that is gone.
    stop ??= fx.register("burst", (dt) => {
        try { step(dt); } catch { finish(); }
    });
}

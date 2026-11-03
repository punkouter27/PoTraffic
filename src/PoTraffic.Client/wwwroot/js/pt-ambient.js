// pt-ambient.js — the dashboard's ambient background: one wash for every commute.
//
// WHY: the dashboard is a list of cards, and each card answers for one route. What it
// cannot say is "how is everything right now" — the question people actually ask when
// they glance at it on the way out. The wash behind the cards answers that without a
// word: a slow, cool drift when every route is running clear, warmer and more agitated
// as the routes run over their usual times. COLOUR carries the verdict and MOTION
// carries the urgency, so it still reads when the motion is stilled.
//
// Rendering: a WebGL2 fragment shader (two octaves of domain-warped value noise) on a
// fixed canvas BEHIND the page, drawn at half resolution and 30fps on the shared
// ticker — it is a blur-soft gradient, so neither the pixels nor the frames are
// visible. Its absence is the static fallback: the host element carries a CSS radial
// gradient in the same tint, which is all anyone sees when WebGL2 is missing, when
// motion is "reduced" (prefers-reduced-motion included) or before the first frame.
// Motion "off" hides the host entirely (pt-motion.css).

import * as fx from "./pt-fx.js";

const VERT = `#version 300 es
// Fullscreen triangle from gl_VertexID: no buffer, no attribute.
void main() {
    vec2 p = vec2((gl_VertexID << 1) & 2, gl_VertexID & 2);
    gl_Position = vec4(p * 2.0 - 1.0, 0.0, 1.0);
}`;

const FRAG = `#version 300 es
precision mediump float;

uniform vec2  u_res;
uniform float u_time;
uniform float u_sev;       // 0 clear … 1 jammed, eased on the JS side
uniform float u_strength;  // theme: dark surfaces carry more colour than light ones
uniform vec3  u_calm;
uniform vec3  u_mid;
uniform vec3  u_hot;

out vec4 fragColor;

float hash(vec2 p) {
    p = fract(p * vec2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return fract(p.x * p.y);
}

float noise(vec2 p) {
    vec2 i = floor(p), f = fract(p);
    vec2 u = f * f * (3.0 - 2.0 * f);
    return mix(mix(hash(i), hash(i + vec2(1, 0)), u.x),
               mix(hash(i + vec2(0, 1)), hash(i + vec2(1, 1)), u.x), u.y);
}

float fbm(vec2 p) {
    return noise(p) * 0.65 + noise(p * 2.03 + 7.1) * 0.35;
}

void main() {
    vec2 uv = gl_FragCoord.xy / u_res;
    vec2 p = uv * vec2(u_res.x / u_res.y, 1.0) * 1.4;

    // Heavier traffic flows faster and folds over itself more: urgency, not just colour.
    float speed = mix(0.035, 0.14, u_sev);
    float warp = mix(0.7, 1.8, u_sev);
    float t = u_time * speed;
    float n = fbm(p + vec2(t, t * 0.6) + warp * fbm(p * 1.6 - vec2(t * 1.3, -t)));

    vec3 tint = u_sev < 0.5 ? mix(u_calm, u_mid, u_sev * 2.0) : mix(u_mid, u_hot, u_sev * 2.0 - 1.0);

    // Strongest along the top of the viewport, where the page header and the first
    // cards sit, fading toward the bottom so long lists stay on a quiet surface.
    float a = smoothstep(0.2, 0.9, n) * mix(0.10, 0.26, u_sev + 0.25) * u_strength;
    a *= mix(0.45, 1.0, uv.y);
    fragColor = vec4(tint * a, a);   // premultiplied
}`;

/** Resolution the wash is drawn at, relative to CSS px. It is soft; nobody sees the pixels. */
const RENDER_SCALE = 0.5;

/** Severity used while no route has a current reading: calm, not neutral grey. */
const DEFAULT_SEVERITY = 0.15;

/** Keyed by the component's placeholder element. @type {WeakMap<HTMLElement, object>} */
const states = new WeakMap();

function ramp(host) {
    return {
        calm: fx.resolveRgb(host, "var(--pt-traffic-clear)", [0.08, 0.5, 0.24]),
        mid: fx.resolveRgb(host, "var(--pt-traffic-slow)", [0.7, 0.33, 0.04]),
        hot: fx.resolveRgb(host, "var(--pt-traffic-heavy)", [0.73, 0.11, 0.11]),
    };
}

function mixRgb(colours, sev) {
    const { calm, mid, hot } = colours;
    const [a, b, k] = sev < 0.5 ? [calm, mid, sev * 2] : [mid, hot, sev * 2 - 1];
    return a.map((c, i) => Math.round((c + (b[i] - c) * k) * 255));
}

/** The CSS fallback's tint, kept in step with the shader's so a switch between them is invisible. */
function paintStatic(state) {
    const [r, g, b] = mixRgb(state.colours, state.target);
    state.host.style.setProperty("--pt-ambient-tint", `rgb(${r} ${g} ${b})`);
}

function startGl(state) {
    const canvas = document.createElement("canvas");
    canvas.className = "pt-ambient-canvas";
    canvas.setAttribute("aria-hidden", "true");

    const gl = fx.gl2(canvas);
    const program = gl && fx.buildProgram(gl, VERT, FRAG);
    if (!program) {
        gl?.getExtension("WEBGL_lose_context")?.loseContext();
        return false;
    }

    state.host.appendChild(canvas);
    const vao = gl.createVertexArray();
    const u = (name) => gl.getUniformLocation(program, name);
    const uniforms = {
        res: u("u_res"), time: u("u_time"), sev: u("u_sev"), strength: u("u_strength"),
        calm: u("u_calm"), mid: u("u_mid"), hot: u("u_hot"),
    };

    gl.enable(gl.BLEND);
    gl.blendFunc(gl.ONE, gl.ONE_MINUS_SRC_ALPHA);

    Object.assign(state, { canvas, gl, program, vao, uniforms, time: Math.random() * 100 });

    state.unregister = fx.register("ambient", (dt) => {
        if (!canvas.isConnected) { stopGl(state); return; }
        // Ease toward the new verdict over a couple of seconds: a refresh that moves
        // the mood should be felt, not seen as a jump.
        state.current += (state.target - state.current) * Math.min(1, dt * 0.8);
        state.time += dt;

        const w = Math.max(1, Math.round(window.innerWidth * RENDER_SCALE));
        const h = Math.max(1, Math.round(window.innerHeight * RENDER_SCALE));
        if (canvas.width !== w || canvas.height !== h) { canvas.width = w; canvas.height = h; }

        gl.viewport(0, 0, w, h);
        gl.clearColor(0, 0, 0, 0);
        gl.clear(gl.COLOR_BUFFER_BIT);
        gl.useProgram(program);
        gl.bindVertexArray(vao);
        gl.uniform2f(uniforms.res, w, h);
        gl.uniform1f(uniforms.time, state.time);
        gl.uniform1f(uniforms.sev, state.current);
        gl.uniform1f(uniforms.strength,
            document.documentElement.getAttribute("data-theme") === "dark" ? 1.0 : 0.75);
        gl.uniform3fv(uniforms.calm, state.colours.calm);
        gl.uniform3fv(uniforms.mid, state.colours.mid);
        gl.uniform3fv(uniforms.hot, state.colours.hot);
        gl.drawArrays(gl.TRIANGLES, 0, 3);
    }, { fps: 30 });

    state.host.setAttribute("data-live", "");
    return true;
}

function stopGl(state) {
    state.unregister?.();
    state.unregister = null;
    if (state.gl) {
        state.gl.deleteVertexArray(state.vao);
        state.gl.deleteProgram(state.program);
        state.gl.getExtension("WEBGL_lose_context")?.loseContext();
    }
    state.canvas?.remove();
    Object.assign(state, { canvas: null, gl: null, program: null, vao: null });
    state.host.removeAttribute("data-live");
}

/** Live shader at full motion, static gradient otherwise. Re-run when the level or theme flips. */
function applyMode(state) {
    state.colours = ramp(state.host);
    paintStatic(state);
    const wantLive = fx.animates();
    if (wantLive && !state.gl) startGl(state);
    else if (!wantLive && state.gl) stopGl(state);
}

/**
 * Starts the wash for the component whose placeholder is `anchor`.
 *
 * The wash itself is a <div> appended to <body>, not the component's own element.
 * Anything between #app and the dashboard that animates a transform (the page
 * entrance, a view transition) becomes the containing block for `position: fixed`
 * and a stacking context for `z-index: -1` — the wash would scroll with the page and
 * paint over its parent's background. At body level neither can happen. Blazor never
 * sees the element, so it never diffs against it.
 *
 * @param {HTMLElement} anchor
 * @param {number|null} severity  0–1 across every route, or null when none has a current reading
 */
export function mount(anchor, severity) {
    if (!anchor) return;
    if (states.has(anchor)) { update(anchor, severity); return; }

    const host = document.createElement("div");
    host.className = "pt-ambient";
    host.setAttribute("aria-hidden", "true");
    document.body.prepend(host);

    const sev = Number.isFinite(severity) ? severity : DEFAULT_SEVERITY;
    const state = {
        host, target: sev, current: sev, colours: ramp(host),
        unregister: null, canvas: null, gl: null,
    };
    states.set(anchor, state);

    // The motion setting and the theme are both stamped on <html>. Watching the two
    // attributes lets Settings change either without the dashboard re-mounting.
    state.observer = new MutationObserver(() => applyMode(state));
    state.observer.observe(document.documentElement, { attributes: true, attributeFilter: ["data-motion", "data-theme"] });

    applyMode(state);
    update(anchor, severity);
}

/** Moves the wash toward a new severity. Also becomes the app-wide mood the map flow falls back on. */
export function update(anchor, severity) {
    const state = anchor && states.get(anchor);
    if (!state) return;
    const known = Number.isFinite(severity);
    state.target = known ? Math.max(0, Math.min(1, severity)) : DEFAULT_SEVERITY;
    if (!state.gl) state.current = state.target;
    paintStatic(state);
    if (known) fx.setMoodValue(state.target);
}

/** Releases the GL context, the ticker slot, the observer and the wash element. Safe to call twice. */
export function unmount(anchor) {
    const state = anchor && states.get(anchor);
    if (!state) return;
    state.observer.disconnect();
    stopGl(state);
    state.host.remove();
    states.delete(anchor);
}

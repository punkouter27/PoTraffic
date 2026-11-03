// pt-flow.js — animated traffic flow along the route line.
//
// Glowing particles travel the polyline from origin to destination. Their SPEED is
// the message: fast and evenly spaced when the commute is clear, slow and bunched
// when it is jammed. That mapping is the whole reason this exists — a coloured line
// says "bad", a crawling line says "bad, and this is what bad feels like".
//
// Drawn in Leaflet's overlay pane by the best renderer the browser has, tried in order:
//   1. WebGPU — one instanced draw of camera-facing quads (WebGPU has no point
//      sprites), additive blend, the same two-stage falloff in WGSL.
//   2. WebGL2 — additive point sprites.
//   3. Canvas2D — the same particles as radial gradients.
// All three draw the same picture from the same per-frame Float32Array; only the
// upload differs. A canvas can hold exactly one kind of context, and WebGPU's adapter
// request is asynchronous, so nothing claims the canvas until that answer is in — a
// frame or two of no particles, never a canvas stuck with the wrong context. A WebGPU
// device lost mid-session (driver reset, GPU process crash) swaps in a fresh canvas
// on the WebGL2 path rather than leaving the line dark.
//
// PERFORMANCE: the particle positions are advanced in JS (a few dozen floats) and
// uploaded once per frame as a single buffer. There is no per-particle draw call and
// no per-particle DOM node — the classic way this effect gets written, and the
// reason it usually drops frames on a phone.

import * as fx from "./pt-fx.js";

const VERT = `#version 300 es
in vec2  a_pos;      // clip space
in float a_size;     // px
in float a_alpha;

uniform float u_dpr;

out float v_alpha;

void main() {
    v_alpha = a_alpha;
    gl_Position = vec4(a_pos, 0.0, 1.0);
    gl_PointSize = a_size * u_dpr;
}`;

const FRAG = `#version 300 es
precision mediump float;

uniform vec3 u_colour;

in  float v_alpha;
out vec4  fragColor;

void main() {
    // gl_PointCoord is 0–1 across the sprite; turn it into a radius from the centre.
    float d = length(gl_PointCoord - vec2(0.5)) * 2.0;

    // Two-stage falloff: a bright tight core inside a wide soft halo. A single
    // smoothstep gives a flat disc that reads as a dot, not as a light.
    float core = 1.0 - smoothstep(0.0, 0.35, d);
    float halo = 1.0 - smoothstep(0.0, 1.0, d);
    float intensity = core * 0.85 + halo * halo * 0.5;

    if (intensity <= 0.002) discard;

    float a = intensity * v_alpha;
    fragColor = vec4(u_colour * a, a);   // premultiplied, blended additively
}`;

const WGSL = `
struct Uniforms {
    px: vec2f,       // clip-space units per device pixel (2/width, 2/height)
    dpr: f32,
    pad: f32,
    colour: vec4f,
};
@group(0) @binding(0) var<uniform> u: Uniforms;

struct VOut {
    @builtin(position) pos: vec4f,
    @location(0) corner: vec2f,
    @location(1) alpha: f32,
};

@vertex
fn vs(@builtin(vertex_index) vi: u32,
      @location(0) centre: vec2f,
      @location(1) size: f32,
      @location(2) alpha: f32) -> VOut {
    var corners = array<vec2f, 6>(
        vec2f(-1.0, -1.0), vec2f(1.0, -1.0), vec2f(-1.0, 1.0),
        vec2f(-1.0, 1.0), vec2f(1.0, -1.0), vec2f(1.0, 1.0));
    let c = corners[vi];
    var o: VOut;
    // size is the sprite's diameter in CSS px, exactly as gl_PointSize uses it.
    o.pos = vec4f(centre + c * (size * u.dpr * 0.5) * u.px, 0.0, 1.0);
    o.corner = c;
    o.alpha = alpha;
    return o;
}

@fragment
fn fs(v: VOut) -> @location(0) vec4f {
    let d = length(v.corner);
    let core = 1.0 - smoothstep(0.0, 0.35, d);
    let halo = 1.0 - smoothstep(0.0, 1.0, d);
    let intensity = core * 0.85 + halo * halo * 0.5;
    if (intensity <= 0.002) { discard; }
    let a = intensity * v.alpha;
    return vec4f(u.colour.rgb * a, a);   // premultiplied, blended additively
}`;

/**
 * Builds the WebGPU path on `canvas`, or resolves null (never rejects) so the
 * caller can fall through to WebGL2 on the same, still-unclaimed canvas.
 */
async function createGpuRenderer(canvas, maxParticles) {
    if (!navigator.gpu) return null;
    let device = null;
    try {
        const adapter = await navigator.gpu.requestAdapter({ powerPreference: "low-power" });
        if (!adapter) return null;
        device = await adapter.requestDevice();

        const context = canvas.getContext("webgpu");
        if (!context) { device.destroy(); return null; }
        const format = navigator.gpu.getPreferredCanvasFormat();
        // Premultiplied so the transparent clear really is transparent over the tiles.
        context.configure({ device, format, alphaMode: "premultiplied" });

        const module = device.createShaderModule({ code: WGSL });
        const pipeline = await device.createRenderPipelineAsync({
            layout: "auto",
            vertex: {
                module, entryPoint: "vs",
                buffers: [{
                    arrayStride: 16,
                    stepMode: "instance",
                    attributes: [
                        { shaderLocation: 0, offset: 0, format: "float32x2" },
                        { shaderLocation: 1, offset: 8, format: "float32" },
                        { shaderLocation: 2, offset: 12, format: "float32" },
                    ],
                }],
            },
            fragment: {
                module, entryPoint: "fs",
                targets: [{
                    format,
                    // Additive, as on the WebGL2 path: a bunched-up jam glows hotter.
                    blend: {
                        color: { srcFactor: "one", dstFactor: "one", operation: "add" },
                        alpha: { srcFactor: "one", dstFactor: "one", operation: "add" },
                    },
                }],
            },
            primitive: { topology: "triangle-list" },
        });

        const instances = device.createBuffer({
            size: maxParticles * 16,
            usage: GPUBufferUsage.VERTEX | GPUBufferUsage.COPY_DST,
        });
        const uniforms = device.createBuffer({
            size: 32,
            usage: GPUBufferUsage.UNIFORM | GPUBufferUsage.COPY_DST,
        });
        const bindGroup = device.createBindGroup({
            layout: pipeline.getBindGroupLayout(0),
            entries: [{ binding: 0, resource: { buffer: uniforms } }],
        });

        return { device, context, pipeline, instances, uniforms, bindGroup, uniformData: new Float32Array(8) };
    } catch {
        // A pipeline that failed to compile throws here. The canvas may now be claimed
        // by "webgpu"; the caller checks for that and starts on a fresh one.
        device?.destroy();
        return null;
    }
}

/** Particle counts. Fewer when the device has already shown it cannot keep up. */
const COUNT_FULL = 44;
const COUNT_DEGRADED = 16;

/** Metres-per-second-ish travel along the path, by mood. Tuned by eye, not by physics. */
const SPEED_CLEAR = 0.19;   // fraction of the path per second
const SPEED_JAMMED = 0.035;

/**
 * The slow zone around an incident: how much of the path it spans (Gaussian width, as a
 * fraction of the path) and the speed at its centre. Slower there means denser there —
 * the particles bunch into a queue behind the incident without being told to.
 */
const INCIDENT_WIDTH = 0.045;
const INCIDENT_SPEED = 0.18;

export function createFlow(L) {
    /**
     * A Leaflet layer that owns one canvas in its own pane. Extending L.Layer rather
     * than drawing into the map's own canvas keeps Leaflet in charge of pane order,
     * so the flow always sits above the tiles and below the markers.
     */
    return L.Layer.extend({

        initialize(options) {
            this._level = options?.level ?? "unknown";
            this._latlngs = options?.latlngs ?? [];
            this._colour = options?.colour ?? "#3b82f6";
            this._incident = options?.incident ?? null;   // [lat, lng] or null
            this._particles = [];
            this._unregister = null;
        },

        onAdd(map) {
            this._map = map;

            this._removed = false;
            this._createCanvas();
            this._seed();
            this._initRenderer();

            // Redraw on any view change. During a pinch or a fling Leaflet fires these
            // continuously, which is fine — the draw is one buffer upload.
            map.on("moveend zoomend resize", this._reset, this);
            map.on("zoomanim", this._onZoomAnim, this);
            // `load` fires once the first tiles and the real container size exist.
            map.on("load", this._reset, this);

            this._reset();
            this._start();
        },

        onRemove(map) {
            this._removed = true;
            this._stop();
            map.off("moveend zoomend resize", this._reset, this);
            map.off("zoomanim", this._onZoomAnim, this);
            map.off("load", this._reset, this);
            this._releaseRenderer();
            this._canvas?.remove();
            this._canvas = null;
        },

        /** Swaps the path and verdict without tearing the layer down. */
        setPath(latlngs, level, colour, incident) {
            this._latlngs = latlngs;
            this._level = level;
            this._colour = colour;
            this._incident = incident ?? null;
            this._seed();
            this._reset();
            this._start();
        },

        // ── Renderer setup ───────────────────────────────────────────────────

        _createCanvas() {
            const pane = this._map.getPane("overlayPane");
            const canvas = this._canvas = L.DomUtil.create("canvas", "pt-flow-canvas");
            canvas.setAttribute("aria-hidden", "true");
            pane.appendChild(canvas);
        },

        /**
         * WebGPU first, then WebGL2, then (implicitly, in _step) canvas2D. A device
         * that has already shed detail skips WebGPU: the adapter request alone can
         * cost more than this layer would save.
         */
        async _initRenderer() {
            this._ready = false;
            const canvas = this._canvas;

            const gpu = fx.isDegraded() ? null : await createGpuRenderer(canvas, COUNT_FULL);
            // Removed, or the canvas replaced, while the adapter was being asked for.
            if (this._removed || canvas !== this._canvas) {
                gpu?.device.destroy();
                return;
            }

            if (gpu) {
                this._gpu = gpu;
                gpu.device.lost.then((info) => {
                    if (this._removed || info.reason === "destroyed" || this._gpu !== gpu) return;
                    this._gpu = null;
                    this._replaceCanvas();
                    this._initGl();
                    this._reset();
                });
            } else {
                // A failed WebGPU attempt can leave this canvas claimed by "webgpu", where
                // getContext("webgl2") returns null forever. Start the fallback on a clean one.
                if (navigator.gpu) this._replaceCanvas();
                this._initGl();
            }

            this._ready = true;
            this._reset();
        },

        _replaceCanvas() {
            this._canvas?.remove();
            this._createCanvas();
        },

        _releaseRenderer() {
            this._ready = false;
            if (this._gpu) {
                const gpu = this._gpu;
                this._gpu = null;
                gpu.instances.destroy();
                gpu.uniforms.destroy();
                gpu.device.destroy();
            }
            this._gl?.getExtension("WEBGL_lose_context")?.loseContext();
            this._gl = null;
            this._ctx = null;
        },

        _initGl() {
            const gl = fx.gl2(this._canvas);
            if (!gl) return;   // canvas2D fallback below handles it

            const program = fx.buildProgram(gl, VERT, FRAG);
            if (!program) return;

            this._gl = gl;
            this._program = program;
            this._buffer = gl.createBuffer();
            this._vao = gl.createVertexArray();

            gl.bindVertexArray(this._vao);
            gl.bindBuffer(gl.ARRAY_BUFFER, this._buffer);

            const stride = 4 * 4;   // pos.xy, size, alpha
            const bind = (name, size, offset) => {
                const loc = gl.getAttribLocation(program, name);
                gl.enableVertexAttribArray(loc);
                gl.vertexAttribPointer(loc, size, gl.FLOAT, false, stride, offset);
            };
            bind("a_pos", 2, 0);
            bind("a_size", 1, 8);
            bind("a_alpha", 1, 12);

            gl.enable(gl.BLEND);
            // Additive: overlapping particles brighten rather than occlude, which is
            // what makes a bunched-up jam glow hotter than free-flowing traffic.
            gl.blendFunc(gl.ONE, gl.ONE);

            // Clear to fully transparent immediately. This canvas sits in Leaflet's
            // overlay pane, ABOVE the tiles — anything but transparent here hides the
            // map. Doing it now means a canvas that never receives an animation frame
            // (reduced motion, a route with no path, a device that throttles rAF) is
            // still invisible rather than showing whatever the buffer started as.
            gl.clearColor(0, 0, 0, 0);
            gl.clear(gl.COLOR_BUFFER_BIT);

            this._uDpr = gl.getUniformLocation(program, "u_dpr");
            this._uColour = gl.getUniformLocation(program, "u_colour");
        },

        // ── Particles ────────────────────────────────────────────────────────

        _speed() {
            // Mood drives speed, but the layer's own verdict wins when it has one —
            // a route page shows one route, and its line should describe that route.
            const mood = { clear: 0.05, normal: 0.3, slow: 0.65, heavy: 0.95 }[this._level]
                ?? fx.getMood();
            return SPEED_CLEAR + (SPEED_JAMMED - SPEED_CLEAR) * mood;
        },

        _seed() {
            const count = fx.isDegraded() ? COUNT_DEGRADED : COUNT_FULL;
            const jam = { clear: 0, normal: 0.25, slow: 0.6, heavy: 1 }[this._level] ?? 0.3;

            this._particles = Array.from({ length: count }, (_, i) => {
                // Even spacing at a clear commute; increasingly clumped as it jams, so
                // the line visibly develops the stop-start clusters of real congestion.
                const even = i / count;
                const clumped = Math.pow(even, 1 + jam * 1.6);
                return {
                    t: even * (1 - jam) + clumped * jam,
                    // Per-particle speed jitter, widening with the jam. Identical speeds
                    // read as a machine; a spread reads as traffic.
                    rate: 1 + (Math.random() - 0.5) * (0.15 + jam * 0.9),
                    size: 5 + Math.random() * 4,
                    phase: Math.random() * Math.PI * 2,
                };
            });
        },

        /**
         * Precomputes the path in container pixels plus its cumulative arc length, so
         * per-frame work is a binary search and a lerp rather than a full re-projection
         * of every vertex.
         */
        _reset() {
            if (!this._canvas || !this._map) return;

            const map = this._map;
            const size = map.getSize();
            const scale = fx.dpr();

            // Leaflet reports a zero size until its container has been laid out —
            // which is the normal state on the first render inside a card that is
            // still settling. Sizing the canvas to 0 there leaves an overlay that
            // never draws again, so bail and let the next moveend/resize retry.
            if (size.x <= 0 || size.y <= 0) return;

            this._canvas.width = Math.max(1, Math.round(size.x * scale));
            this._canvas.height = Math.max(1, Math.round(size.y * scale));
            this._canvas.style.width = `${size.x}px`;
            this._canvas.style.height = `${size.y}px`;

            // The overlay pane is translated as the map pans; undo that so the canvas
            // stays pinned to the viewport and our pixel coordinates stay valid.
            const corner = map.containerPointToLayerPoint([0, 0]);
            L.DomUtil.setPosition(this._canvas, corner);

            this._points = this._latlngs.map((ll) => map.latLngToContainerPoint(ll));

            this._lengths = [0];
            let total = 0;
            for (let i = 1; i < this._points.length; i++) {
                total += this._points[i].distanceTo(this._points[i - 1]);
                this._lengths.push(total);
            }
            this._total = total;
            this._incidentT = this._incident ? this._nearestT(map.latLngToContainerPoint(this._incident)) : null;

            // WebGPU needs nothing here: getCurrentTexture() follows the canvas size.
            this._gl?.viewport(0, 0, this._canvas.width, this._canvas.height);
            this._colourRgb = fx.resolveRgb(this._canvas.parentElement, this._colour, [0.23, 0.51, 0.96]);
        },

        _onZoomAnim(e) {
            // Leaflet animates the zoom by transforming the pane. Our canvas geometry is
            // computed in the OLD projection, so leave it hidden until zoomend re-runs
            // _reset — a stale flow smeared across a zooming map looks broken.
            this._canvas.style.opacity = "0";
            clearTimeout(this._zoomTimer);
            this._zoomTimer = setTimeout(() => {
                if (this._canvas) this._canvas.style.opacity = "";
            }, 260);
        },

        /** Position along the path at fraction t, in container px. */
        _at(t) {
            const target = t * this._total;
            const lengths = this._lengths;

            // Binary search the cumulative-length table.
            let lo = 0, hi = lengths.length - 1;
            while (lo < hi - 1) {
                const mid = (lo + hi) >> 1;
                if (lengths[mid] <= target) lo = mid; else hi = mid;
            }

            const span = lengths[hi] - lengths[lo];
            const f = span > 0 ? (target - lengths[lo]) / span : 0;
            const a = this._points[lo];
            const b = this._points[hi];
            return [a.x + (b.x - a.x) * f, a.y + (b.y - a.y) * f];
        },

        /** Fraction along the path of the point nearest `pt`, in container px. */
        _nearestT(pt) {
            let best = Infinity, bestT = 0;
            for (let i = 1; i < this._points.length; i++) {
                const a = this._points[i - 1], b = this._points[i];
                const dx = b.x - a.x, dy = b.y - a.y;
                const lenSq = dx * dx + dy * dy;
                const f = lenSq ? Math.max(0, Math.min(1, ((pt.x - a.x) * dx + (pt.y - a.y) * dy) / lenSq)) : 0;
                const d = Math.hypot(a.x + dx * f - pt.x, a.y + dy * f - pt.y);
                if (d < best) {
                    best = d;
                    bestT = this._total ? (this._lengths[i - 1] + (this._lengths[i] - this._lengths[i - 1]) * f) / this._total : 0;
                }
            }
            return bestT;
        },

        /** Speed multiplier at t: 1 on open road, INCIDENT_SPEED at the incident itself. */
        _slowAt(t) {
            if (this._incidentT === null || this._incidentT === undefined) return 1;
            const z = (t - this._incidentT) / INCIDENT_WIDTH;
            return 1 - (1 - INCIDENT_SPEED) * Math.exp(-z * z);
        },

        // ── Frame ────────────────────────────────────────────────────────────

        _start() {
            this._stop();
            if (!fx.animates() || this._latlngs.length < 2) return;
            this._unregister = fx.register("flow", (dt, now) => this._step(dt, now));
        },

        _stop() {
            this._unregister?.();
            this._unregister = null;
            clearTimeout(this._zoomTimer);
        },

        _step(dt, now) {
            if (!this._canvas || !this._total || !this._ready) return;
            if (this._canvas.width <= 0 || this._canvas.height <= 0) { this._reset(); return; }

            const speed = this._speed();
            const data = new Float32Array(this._particles.length * 4);
            const w = this._canvas.width;
            const h = this._canvas.height;
            const scale = fx.dpr();

            for (let i = 0; i < this._particles.length; i++) {
                const p = this._particles[i];
                p.t += speed * p.rate * this._slowAt(p.t) * dt;
                if (p.t > 1) p.t -= 1;

                const [x, y] = this._at(p.t);

                // Fade in and out at the ends so particles do not pop into existence at
                // the origin pin and vanish at the destination pin.
                const edge = Math.min(p.t, 1 - p.t);
                const fade = Math.min(1, edge / 0.06);

                // Slow breathing, out of phase per particle, so a still line still lives.
                const pulse = 0.72 + 0.28 * Math.sin(now * 0.003 + p.phase);

                const o = i * 4;
                data[o] = (x * scale / w) * 2 - 1;
                data[o + 1] = 1 - (y * scale / h) * 2;    // GL's Y points the other way
                data[o + 2] = p.size * pulse;
                data[o + 3] = fade * pulse * 0.9;
            }

            if (this._gpu) this._drawGpu(data);
            else if (this._gl) this._drawGl(data);
            else this._draw2d(data);
        },

        _drawGpu(data) {
            const gpu = this._gpu;
            const count = Math.min(data.length / 4, COUNT_FULL);
            const u = gpu.uniformData;
            u[0] = 2 / this._canvas.width;
            u[1] = 2 / this._canvas.height;
            u[2] = fx.dpr();
            u[4] = this._colourRgb[0];
            u[5] = this._colourRgb[1];
            u[6] = this._colourRgb[2];
            u[7] = 1;

            gpu.device.queue.writeBuffer(gpu.uniforms, 0, u);
            gpu.device.queue.writeBuffer(gpu.instances, 0, data, 0, count * 4);

            const encoder = gpu.device.createCommandEncoder();
            const pass = encoder.beginRenderPass({
                colorAttachments: [{
                    view: gpu.context.getCurrentTexture().createView(),
                    clearValue: { r: 0, g: 0, b: 0, a: 0 },
                    loadOp: "clear",
                    storeOp: "store",
                }],
            });
            pass.setPipeline(gpu.pipeline);
            pass.setBindGroup(0, gpu.bindGroup);
            pass.setVertexBuffer(0, gpu.instances);
            pass.draw(6, count);
            pass.end();
            gpu.device.queue.submit([encoder.finish()]);
        },

        _drawGl(data) {
            const gl = this._gl;
            gl.clearColor(0, 0, 0, 0);
            gl.clear(gl.COLOR_BUFFER_BIT);

            gl.useProgram(this._program);
            gl.bindVertexArray(this._vao);
            gl.bindBuffer(gl.ARRAY_BUFFER, this._buffer);
            gl.bufferData(gl.ARRAY_BUFFER, data, gl.DYNAMIC_DRAW);

            gl.uniform1f(this._uDpr, fx.dpr());
            gl.uniform3fv(this._uColour, this._colourRgb);

            gl.drawArrays(gl.POINTS, 0, data.length / 4);
        },

        /** No WebGL2: same particles, radial gradients instead of a shader. */
        _draw2d(data) {
            const ctx = this._ctx ??= this._canvas.getContext("2d");
            if (!ctx) return;

            const w = this._canvas.width;
            const h = this._canvas.height;
            const scale = fx.dpr();
            const [r, g, b] = this._colourRgb.map((c) => Math.round(c * 255));

            ctx.clearRect(0, 0, w, h);
            ctx.globalCompositeOperation = "lighter";

            for (let i = 0; i < data.length; i += 4) {
                const x = ((data[i] + 1) / 2) * w;
                const y = ((1 - data[i + 1]) / 2) * h;
                const radius = data[i + 2] * scale;
                const alpha = data[i + 3];

                const grad = ctx.createRadialGradient(x, y, 0, x, y, radius);
                grad.addColorStop(0, `rgba(${r},${g},${b},${alpha})`);
                grad.addColorStop(0.35, `rgba(${r},${g},${b},${alpha * 0.5})`);
                grad.addColorStop(1, `rgba(${r},${g},${b},0)`);

                ctx.fillStyle = grad;
                ctx.beginPath();
                ctx.arc(x, y, radius, 0, Math.PI * 2);
                ctx.fill();
            }

            ctx.globalCompositeOperation = "source-over";
        },
    });
}

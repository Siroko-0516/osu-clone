let animationFrame;
let canvas;
let gl;
let dotnet;
let program;
let frameworkProgram;
let frameworkBuffer;
let frame = 0;
let pumpPending = false;
let pointer = { x: 0, y: 0, down: false };\nlet activePointerId = null;
const keys = new Set();
let ruleset = "osu";
let frameworkFrame;
const listeners = [];
let lastPointerReport = 0;
let lastKeyboardState = "";

const rulesetKeys = {
    osu: new Set(["KeyZ", "KeyX"]),
    mania: new Set(["KeyD", "KeyF", "KeyJ", "KeyK"]),
    taiko: new Set(["KeyZ", "KeyX", "KeyC", "KeyV"]),
    catch: new Set(["ArrowLeft", "ArrowRight"])
};

function reportKeyboardState(action) {
    const active = [...keys].sort().join(" + ") || "none";
    const state = `${action} · held: ${active}`;

    if (state === lastKeyboardState || !dotnet)
        return;

    lastKeyboardState = state;
    dotnet.invokeMethodAsync("ReportInput", state, pointer.x, pointer.y);
}

function releaseAllKeys(action = "keyboard reset") {
    if (keys.size === 0)
        return;

    keys.clear();
    reportKeyboardState(action);
}

const vertexSource = `#version 300 es
in vec2 a_position;
void main() { gl_Position = vec4(a_position, 0.0, 1.0); }`;

const fragmentSource = `#version 300 es
precision highp float;
uniform vec2 u_resolution;
uniform vec2 u_pointer;
uniform float u_time;
uniform float u_down;
uniform float u_keys;
out vec4 colour;

void main() {
    vec2 uv = gl_FragCoord.xy / u_resolution;
    vec2 centred = uv - .5;
    centred.x *= u_resolution.x / u_resolution.y;
    float glow = exp(-length(centred) * (3.2 + sin(u_time) * .25));
    vec3 background = mix(vec3(.025, .018, .035), vec3(.22, .075, .17), glow);

    vec2 grid = abs(fract(gl_FragCoord.xy / 40.0) - .5) / fwidth(gl_FragCoord.xy / 40.0);
    float line = 1.0 - min(min(grid.x, grid.y), 1.0);
    background += vec3(line * .035);

    vec2 cursor = vec2(u_pointer.x, u_resolution.y - u_pointer.y);
    float distanceToCursor = distance(gl_FragCoord.xy, cursor);
    float radius = mix(18.0, 26.0, u_down);
    float disc = 1.0 - smoothstep(radius - 1.5, radius + 1.5, distanceToCursor);
    float rim = smoothstep(radius - 7.0, radius - 4.0, distanceToCursor) * disc;
    vec3 cursorColour = mix(vec3(.94, .28, .59), vec3(1.0), u_down);
    cursorColour = mix(cursorColour, vec3(.45, .75, 1.0), min(u_keys, 1.0) * .35);
    colour = vec4(mix(background, mix(cursorColour, vec3(1.0), rim), disc), 1.0);
}`;

const frameworkVertexSource = `#version 300 es
in vec2 a_position;
in vec4 a_colour;
uniform vec2 u_viewport;
out vec4 v_colour;
void main() {
    vec2 clip = (a_position / u_viewport) * 2.0 - 1.0;
    gl_Position = vec4(clip.x, -clip.y, 0.0, 1.0);
    v_colour = a_colour;
}`;

const frameworkFragmentSource = `#version 300 es
precision highp float;
in vec4 v_colour;
out vec4 colour;
void main() { colour = v_colour; }`;

function listen(target, event, handler, options) {
    target.addEventListener(event, handler, options);
    listeners.push(() => target.removeEventListener(event, handler, options));
}

function shader(type, source) {
    const result = gl.createShader(type);
    gl.shaderSource(result, source);
    gl.compileShader(result);
    if (!gl.getShaderParameter(result, gl.COMPILE_STATUS))
        throw new Error(gl.getShaderInfoLog(result));
    return result;
}

function createProgram() {
    const result = gl.createProgram();
    gl.attachShader(result, shader(gl.VERTEX_SHADER, vertexSource));
    gl.attachShader(result, shader(gl.FRAGMENT_SHADER, fragmentSource));
    gl.linkProgram(result);
    if (!gl.getProgramParameter(result, gl.LINK_STATUS))
        throw new Error(gl.getProgramInfoLog(result));
    return result;
}

function createFrameworkProgram() {
    const result = gl.createProgram();
    gl.attachShader(result, shader(gl.VERTEX_SHADER, frameworkVertexSource));
    gl.attachShader(result, shader(gl.FRAGMENT_SHADER, frameworkFragmentSource));
    gl.linkProgram(result);
    if (!gl.getProgramParameter(result, gl.LINK_STATUS))
        throw new Error(gl.getProgramInfoLog(result));
    return result;
}

function drawFrameworkFrame(state) {
    const viewportWidth = state[6] > 0 ? state[6] : canvas.width;
    const viewportHeight = state[7] > 0 ? state[7] : canvas.height;
    const source = state.slice(8);
    const triangles = [];

    // osu!framework's quad batches contain four vertices in BL, BR, TR, TL order.
    // Expand them into WebGL triangles while the native indexed batch is being ported.
    for (let offset = 0; offset + 23 < source.length; offset += 24) {
        for (const vertex of [0, 1, 2, 2, 3, 0]) {
            const start = offset + vertex * 6;
            triangles.push(...source.slice(start, start + 6));
        }
    }

    if (triangles.length === 0)
        return;

    gl.useProgram(frameworkProgram);
    gl.bindBuffer(gl.ARRAY_BUFFER, frameworkBuffer);
    gl.bufferData(gl.ARRAY_BUFFER, new Float32Array(triangles), gl.DYNAMIC_DRAW);

    const position = gl.getAttribLocation(frameworkProgram, "a_position");
    const colour = gl.getAttribLocation(frameworkProgram, "a_colour");
    gl.enableVertexAttribArray(position);
    gl.enableVertexAttribArray(colour);
    gl.vertexAttribPointer(position, 2, gl.FLOAT, false, 24, 0);
    gl.vertexAttribPointer(colour, 4, gl.FLOAT, false, 24, 8);
    gl.uniform2f(gl.getUniformLocation(frameworkProgram, "u_viewport"), viewportWidth, viewportHeight);
    gl.drawArrays(gl.TRIANGLES, 0, triangles.length / 6);
}

export async function startBrowserHost(target, dotnetReference) {
    canvas = target;
    dotnet = dotnetReference;
    gl = canvas.getContext("webgl2", { alpha: false, antialias: true, powerPreference: "high-performance" });

    if (!gl) {
        await dotnet.invokeMethodAsync("ReportRenderer", "WebGL2 unavailable");
        return;
    }

    program = createProgram();
    frameworkProgram = createFrameworkProgram();
    frameworkBuffer = gl.createBuffer();
    const buffer = gl.createBuffer();
    gl.bindBuffer(gl.ARRAY_BUFFER, buffer);
    gl.bufferData(gl.ARRAY_BUFFER, new Float32Array([-1,-1, 3,-1, -1,3]), gl.STATIC_DRAW);
    const positionLocation = gl.getAttribLocation(program, "a_position");
    gl.enableVertexAttribArray(positionLocation);
    gl.vertexAttribPointer(positionLocation, 2, gl.FLOAT, false, 0, 0);
    gl.useProgram(program);

    const uniforms = {
        resolution: gl.getUniformLocation(program, "u_resolution"),
        pointer: gl.getUniformLocation(program, "u_pointer"),
        time: gl.getUniformLocation(program, "u_time"),
        down: gl.getUniformLocation(program, "u_down"),
        keys: gl.getUniformLocation(program, "u_keys")
    };

    const resize = () => {
        const ratio = window.devicePixelRatio || 1;
        const rect = canvas.getBoundingClientRect();
        canvas.width = Math.max(1, Math.round(rect.width * ratio));
        canvas.height = Math.max(1, Math.round(rect.height * ratio));
        gl.viewport(0, 0, canvas.width, canvas.height);
    };

    const position = event => {
        const rect = canvas.getBoundingClientRect();
        const ratio = window.devicePixelRatio || 1;
        pointer.x = Math.max(0, Math.min(canvas.width, (event.clientX - rect.left) * ratio));
        pointer.y = Math.max(0, Math.min(canvas.height, (event.clientY - rect.top) * ratio));
    };

    listen(canvas, "pointermove", event => {
        event.preventDefault();
        position(event);

        // Keep the diagnostic text responsive without flooding the .NET bridge.
        const now = performance.now();
        if (now - lastPointerReport >= 50) {
            lastPointerReport = now;
            dotnet.invokeMethodAsync("ReportInput", pointer.down ? "pointer drag" : "pointer move", pointer.x, pointer.y);
        }
    }, { passive: false });
    listen(canvas, "pointerdown", event => {
        if (activePointerId !== null && activePointerId !== event.pointerId)
            return;

        event.preventDefault();
        position(event);
        activePointerId = event.pointerId;
        pointer.down = true;
        canvas.setPointerCapture?.(event.pointerId);
        canvas.focus();
        dotnet.invokeMethodAsync("ReportInput", "pointer down", pointer.x, pointer.y);
    }, { passive: false });
    listen(window, "pointerup", event => {
        if (event.pointerId !== activePointerId)
            return;

        position(event);
        pointer.down = false;
        activePointerId = null;
        dotnet.invokeMethodAsync("ReportInput", "pointer up", pointer.x, pointer.y);
    });
    listen(window, "pointercancel", event => {
        if (event.pointerId !== activePointerId)
            return;

        pointer.down = false;
        activePointerId = null;
        dotnet.invokeMethodAsync("ReportInput", "pointer cancel", pointer.x, pointer.y);
    });
    // Track physical key state ourselves. Browser key-repeat has a platform-defined delay
    // and is unsuitable for rhythm input; a Set also preserves simultaneous key presses.
    listen(window, "keydown", event => {
        if (!rulesetKeys[ruleset]?.has(event.code)) return;
        event.preventDefault();
        if (event.repeat || keys.has(event.code)) return;
        keys.add(event.code);
        reportKeyboardState(`${event.code} down`);
    }, { capture: true });
    listen(window, "keyup", event => {
        if (!keys.delete(event.code)) return;
        event.preventDefault();
        reportKeyboardState(`${event.code} up`);
    }, { capture: true });
    listen(window, "blur", () => releaseAllKeys());
    listen(document, "visibilitychange", () => {
        if (document.hidden) releaseAllKeys();
    });
    listen(window, "resize", resize);
    listen(canvas, "webglcontextlost", event => {
        event.preventDefault();
        dotnet.invokeMethodAsync("ReportRenderer", "WebGL2 context lost");
    });

    resize();
    pointer.x = canvas.width / 2;
    pointer.y = canvas.height / 2;
    await dotnet.invokeMethodAsync("ReportRenderer", `WebGL2 · ${gl.getParameter(gl.RENDERER)}`);

    const render = time => {
        // The first framework frames may contain only the clear colour and viewport
        // while the scene graph is still loading. Keep the diagnostic surface visible
        // until drawable geometry is actually available instead of showing a black box.
        if (frameworkFrame?.length > 8) {
            const [r, g, b, a] = frameworkFrame;
            gl.clearColor(r, g, b, a);
            gl.clear(gl.COLOR_BUFFER_BIT | gl.DEPTH_BUFFER_BIT | gl.STENCIL_BUFFER_BIT);
            drawFrameworkFrame(frameworkFrame);
        } else {
            gl.useProgram(program);
            gl.bindBuffer(gl.ARRAY_BUFFER, buffer);
            gl.enableVertexAttribArray(positionLocation);
            gl.vertexAttribPointer(positionLocation, 2, gl.FLOAT, false, 0, 0);
            gl.uniform2f(uniforms.resolution, canvas.width, canvas.height);
            gl.uniform2f(uniforms.pointer, pointer.x, pointer.y);
            gl.uniform1f(uniforms.time, time / 1000);
            gl.uniform1f(uniforms.down, pointer.down ? 1 : 0);
            gl.uniform1f(uniforms.keys, keys.size);
            gl.drawArrays(gl.TRIANGLES, 0, 3);
        }

        frame++;
        if (!pumpPending) {
            pumpPending = true;
            dotnet.invokeMethodAsync("PumpGameFrame", frame)
                .finally(() => pumpPending = false);
        }
        animationFrame = requestAnimationFrame(render);
    };
    animationFrame = requestAnimationFrame(render);
}

export function stopBrowserHost() {
    if (animationFrame) cancelAnimationFrame(animationFrame);
    for (const remove of listeners.splice(0)) remove();
    animationFrame = undefined;
    pumpPending = false;
    activePointerId = null;
    pointer.down = false;
    dotnet = undefined;
    gl = undefined;
    frameworkProgram = undefined;
    frameworkBuffer = undefined;
}

export function setRuleset(mode) {
    ruleset = mode;
    releaseAllKeys("ruleset changed");
    if (dotnet) dotnet.invokeMethodAsync("ReportInput", `ruleset ${mode}`, pointer.x, pointer.y);
}

export function applyFrameworkFrame(state) {
    frameworkFrame = state;
}

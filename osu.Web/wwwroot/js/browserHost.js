let animationFrame;
let canvas;
let gl;
let dotnet;
let program;
let frame = 0;
let pointer = { x: 0, y: 0, down: false };
const keys = new Set();
let ruleset = "osu";
const listeners = [];

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

export async function startBrowserHost(target, dotnetReference) {
    canvas = target;
    dotnet = dotnetReference;
    gl = canvas.getContext("webgl2", { alpha: false, antialias: true, powerPreference: "high-performance" });

    if (!gl) {
        await dotnet.invokeMethodAsync("ReportRenderer", "WebGL2 unavailable");
        return;
    }

    program = createProgram();
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
        pointer.x = (event.clientX - rect.left) * ratio;
        pointer.y = (event.clientY - rect.top) * ratio;
    };

    listen(canvas, "pointermove", position);
    listen(canvas, "pointerdown", event => {
        position(event);
        pointer.down = true;
        canvas.focus();
        dotnet.invokeMethodAsync("ReportInput", "pointer down", pointer.x, pointer.y);
    });
    listen(window, "pointerup", () => pointer.down = false);
    listen(canvas, "keydown", event => {
        if (!["KeyZ", "KeyX", "KeyD", "KeyF", "KeyJ", "KeyK", "ArrowLeft", "ArrowRight"].includes(event.code)) return;
        event.preventDefault();
        keys.add(event.code);
        dotnet.invokeMethodAsync("ReportInput", event.code, pointer.x, pointer.y);
    });
    listen(canvas, "keyup", event => keys.delete(event.code));
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
        gl.uniform2f(uniforms.resolution, canvas.width, canvas.height);
        gl.uniform2f(uniforms.pointer, pointer.x, pointer.y);
        gl.uniform1f(uniforms.time, time / 1000);
        gl.uniform1f(uniforms.down, pointer.down ? 1 : 0);
        gl.uniform1f(uniforms.keys, keys.size);
        gl.drawArrays(gl.TRIANGLES, 0, 3);

        frame++;
        if (frame % 30 === 0) dotnet.invokeMethodAsync("ReportFrame", frame);
        animationFrame = requestAnimationFrame(render);
    };
    animationFrame = requestAnimationFrame(render);
}

export function stopBrowserHost() {
    if (animationFrame) cancelAnimationFrame(animationFrame);
    for (const remove of listeners.splice(0)) remove();
    animationFrame = undefined;
    dotnet = undefined;
    gl = undefined;
}

export function setRuleset(mode) {
    ruleset = mode;
    if (dotnet) dotnet.invokeMethodAsync("ReportInput", `ruleset ${mode}`, pointer.x, pointer.y);
}

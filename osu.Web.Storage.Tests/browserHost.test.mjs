import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';

const source = await readFile(new URL('../osu.Web/wwwroot/js/browserHost.js', import.meta.url), 'utf8');
const importable = source.replace("'./browserInput.mjs'", JSON.stringify(new URL('../osu.Web/wwwroot/js/browserInput.mjs', import.meta.url).href));
const host = await import(`data:text/javascript;base64,${Buffer.from(importable).toString('base64')}`);

test('only standard combines keyboard hold with pointer drag', () => {
    assert.equal(host.isPointerDrag('osu', false, true), true);
    assert.equal(host.isPointerDrag('mania', false, true), false);
    assert.equal(host.isPointerDrag('taiko', false, true), false);
    assert.equal(host.isPointerDrag('catch', false, true), false);
    assert.equal(host.isPointerDrag('mania', true, false), true);
});

test('renderer startup and texture transport', async () => {
    const reports = [];
    const bridge = { invokeMethodAsync: async (...args) => reports.push(args) };
    assert.equal(await host.startBrowserHost({ getContext: () => null }, bridge), false);
    assert.deepEqual(reports[0], ['ReportRenderer', 'WebGL2 unavailable']);
    host.stopBrowserHost();

    const calls = [];
    const gl = new Proxy({}, {
        get: (_, name) => {
            if (name === name.toUpperCase()) return name;
            if (name === 'getShaderParameter' || name === 'getProgramParameter') return () => true;
            if (name === 'getAttribLocation') return () => 0;
            if (name === 'getParameter') return () => 'test renderer';
            return (...args) => { calls.push([name, ...args]); return {}; };
        }
    });
    globalThis.window = new EventTarget();
    globalThis.document = new EventTarget();
    globalThis.requestAnimationFrame = () => 1;
    globalThis.cancelAnimationFrame = () => {};
    const canvas = new EventTarget();
    canvas.getContext = () => gl;
    canvas.getBoundingClientRect = () => ({ left: 0, top: 0, width: 640, height: 480 });
    try {
        assert.equal(await host.startBrowserHost(canvas, bridge), true);
        assert.ok(calls.some(call => call[0] === 'texParameteri' && call[2] === 'TEXTURE_MIN_FILTER' && call[3] === 'LINEAR'));
        for (const data of ['/wCA/w==', [255, 0, 128, 255], new Uint8Array([255, 0, 128, 255])]) {
            host.applyTextureUploads([{ textureId: 1, textureWidth: 1, textureHeight: 1, x: 0, y: 0, width: 1, height: 1, data }]);
            const pixels = calls.filter(call => call[0] === 'texSubImage2D').at(-1).at(-1);
            assert.ok(pixels instanceof Uint8Array);
            assert.deepEqual([...pixels], [255, 0, 128, 255]);
        }
        host.applyTextureUploads([{ textureId: 2, textureWidth: 8, textureHeight: 8, x: 2, y: 1, width: 1, height: 2, data: new Uint8Array(8) }]);
        const region = calls.filter(call => call[0] === 'texSubImage2D').at(-1);
        assert.equal(region[3], 2);
        assert.equal(region[4], 1, 'atlas Y must agree with top-left framework UVs');
        host.applyTextureUploads([{ textureId: 2, deleted: true }]);
        assert.equal(calls.filter(call => call[0] === 'deleteTexture').length, 1);
    } finally {
        host.stopBrowserHost();
        delete globalThis.window;
        delete globalThis.document;
        delete globalThis.requestAnimationFrame;
        delete globalThis.cancelAnimationFrame;
    }
    assert.doesNotThrow(() => host.applyTextureUploads([]));
});

test('frame buffers plateau, empty frames discard stale geometry, and shutdown releases GPU resources', async () => {
    const calls = [];
    let render;
    const gl = new Proxy({}, { get: (_, name) => {
        if (name === name.toUpperCase()) return name;
        if (name === 'getShaderParameter' || name === 'getProgramParameter') return () => true;
        if (name === 'getAttribLocation') return () => 0;
        if (name === 'getParameter') return () => 'test renderer';
        return (...args) => { calls.push([name, ...args]); return {}; };
    }});
    globalThis.window = new EventTarget();
    globalThis.document = new EventTarget();
    globalThis.requestAnimationFrame = fn => { render = fn; return 1; };
    globalThis.cancelAnimationFrame = () => {};
    const canvas = new EventTarget();
    canvas.getContext = () => gl;
    canvas.getBoundingClientRect = () => ({ left: 0, top: 0, width: 640, height: 480 });
    const bridge = { invokeMethodAsync: async () => {} };
    const packet = quads => {
        const data = new Float32Array(8 + quads * 36);
        data[3] = 1; data[4] = quads * 36; data[6] = 640; data[7] = 480;
        return data;
    };
    try {
        await host.startBrowserHost(canvas, bridge);
        const initial = packet(1000);
        host.applyFrameworkFrame(new Uint8Array(initial.buffer));
        initial.fill(0); // Mutating the interop source must not change the copied frame.
        render(0);
        assert.equal(calls.filter(c => c[0] === 'drawArrays').at(-1)[3], 6000);
        assert.ok(calls.some(c => c[0] === 'enable' && c[1] === 'BLEND'));
        assert.ok(calls.some(c => c[0] === 'blendFuncSeparate' && c[1] === 'SRC_ALPHA' && c[2] === 'ONE_MINUS_SRC_ALPHA'));
        const peak = host.rendererMemory();
        const uploads = calls.filter(c => c[0] === 'bufferData').length;
        for (let i = 0; i < 100; i++) {
            host.applyFrameworkFrame(packet(990 + i % 10));
            render(i + 1);
        }
        assert.equal(host.rendererMemory().frameBytes, peak.frameBytes);
        assert.equal(calls.filter(c => c[0] === 'bufferData').length, uploads,
            'small frame-size fluctuations must not reallocate GPU storage');
        const largeWithEmptyHeader = packet(1000);
        largeWithEmptyHeader[4] = 0;
        host.applyFrameworkFrame(largeWithEmptyHeader);
        const drawCount = calls.filter(c => c[0] === 'drawArrays').length;
        render(102);
        assert.equal(calls.filter(c => c[0] === 'drawArrays').length, drawCount,
            'a zero live count must not draw stale capacity bytes or the fallback scene');
        assert.ok(host.rendererMemory().frameBytes < peak.frameBytes / 4);
        assert.ok(host.rendererMemory().gpuVertexBytes < peak.gpuVertexBytes / 4);
        assert.throws(() => host.applyFrameworkFrame(new Float32Array(2)), /header/);
        host.stopBrowserHost();
        assert.deepEqual(host.rendererMemory(), { frameBytes: 0, vertexBytes: 0, gpuVertexBytes: 0, liveFloats: 0, textures: 0 });
        assert.equal(calls.filter(c => c[0] === 'deleteBuffer').length, 2);
        assert.equal(calls.filter(c => c[0] === 'deleteProgram').length, 2);
        assert.equal(calls.filter(c => c[0] === 'deleteTexture').length, 1);
        assert.equal(calls.filter(c => c[0] === 'deleteShader').length, 4);
        const oldRender = render;
        await host.startBrowserHost(canvas, bridge);
        const count = calls.length;
        oldRender(103);
        assert.equal(calls.length, count, 'a stale animation callback cannot touch a new host');
    } finally {
        host.stopBrowserHost();
        delete globalThis.window;
        delete globalThis.document;
        delete globalThis.requestAnimationFrame;
        delete globalThis.cancelAnimationFrame;
    }
});

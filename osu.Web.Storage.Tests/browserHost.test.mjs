import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';

const source = await readFile(new URL('../osu.Web/wwwroot/js/browserHost.js', import.meta.url), 'utf8');
const importable = source.replace("'./browserInput.mjs'", JSON.stringify(new URL('../osu.Web/wwwroot/js/browserInput.mjs', import.meta.url).href));
const host = await import(`data:text/javascript;base64,${Buffer.from(importable).toString('base64')}`);

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
    } finally {
        host.stopBrowserHost();
        delete globalThis.window;
        delete globalThis.document;
        delete globalThis.requestAnimationFrame;
        delete globalThis.cancelAnimationFrame;
    }
    assert.doesNotThrow(() => host.applyTextureUploads([]));
});

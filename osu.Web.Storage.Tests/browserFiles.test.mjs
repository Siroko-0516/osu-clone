import assert from 'node:assert/strict';
import test from 'node:test';
import { indexedDB } from 'fake-indexeddb';
import { createBrowserFileStore } from '../osu.Web/wwwroot/js/browserFiles.mjs';

test('binary files persist, replace atomically, and reject unsafe paths', async () => {
    const name = `files-${crypto.randomUUID()}`;
    let store = createBrowserFileStore(indexedDB, name);
    assert.equal(await store.replaceAll([
        { path: 'osu-web/tracks/song.bin', data: new Uint8Array([0, 1, 127, 255]) },
        { path: 'osu-web/skins/icon.png', data: new Uint8Array([9, 8, 7]) }
    ]), 2);
    await store.close();

    store = createBrowserFileStore(indexedDB, name);
    let restored = await store.list();
    assert.deepEqual(restored.map(entry => entry.path).sort(), ['osu-web/skins/icon.png', 'osu-web/tracks/song.bin']);
    assert.deepEqual([...restored.find(entry => entry.path.endsWith('song.bin')).data], [0, 1, 127, 255]);
    await assert.rejects(store.replaceAll([{ path: '../escape', data: new Uint8Array([1]) }]));
    restored = await store.list();
    assert.equal(restored.length, 2, 'validation failure must retain the committed snapshot');
    await store.replaceAll([{ path: 'osu-web/only.dat', data: new Uint8Array([5]) }]);
    assert.deepEqual((await store.list()).map(entry => entry.path), ['osu-web/only.dat']);
    await store.close();
});

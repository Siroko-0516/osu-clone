import assert from 'node:assert/strict';
import test from 'node:test';
import * as audio from '../osu.Web/wwwroot/js/browserAudio.mjs';

test('browser track clock, pause, seek-before-load, pitch modes, and disposal', async () => {
    const media = [];
    class FakeAudio extends EventTarget {
        constructor() { super(); this.readyState = 0; this.currentTime = 0; this.duration = NaN; this.paused = true; this.ended = false; media.push(this); }
        async play() { this.paused = false; }
        pause() { this.paused = true; }
        removeAttribute() {}
        load() {}
    }
    const node = () => ({gain:{value:1},pan:{value:0},connect(other){return other;},disconnect(){}});
    globalThis.Audio = FakeAudio;
    globalThis.AudioContext = class {
        state = 'suspended'; destination = {};
        createGain() { return node(); }
        createStereoPanner() { return node(); }
        createMediaElementSource() { return node(); }
        async resume() { this.state = 'running'; }
        async close() { this.state = 'closed'; }
    };
    try {
        const id = audio.createTrack(new Uint8Array([1,2,3]));
        assert.equal(audio.trackState(id).loaded, false);
        assert.equal(audio.seekTrack(id, 2000), true);
        assert.equal(audio.trackState(id).position, 2000);
        media[0].duration = 5;
        media[0].readyState = 4;
        media[0].dispatchEvent(new Event('loadedmetadata'));
        assert.equal(media[0].currentTime, 2);
        audio.playTrack(id);
        await Promise.resolve(); await Promise.resolve();
        assert.equal(audio.trackState(id).running, true);
        media[0].currentTime = 2.5;
        assert.equal(audio.trackState(id).position, 2500);
        audio.stopTrack(id);
        assert.equal(audio.trackState(id).running, false);
        assert.equal(audio.trackState(id).position, 2500);
        assert.equal(audio.seekTrack(id, 6000), false);
        assert.equal(audio.seekTrack(id, -1), false);
        audio.configureTrack(id, .5, 0, 1, 1.5, false);
        assert.equal(media[0].preservesPitch, true);
        assert.equal(media[0].playbackRate, 1.5);
        audio.configureTrack(id, .5, 0, 1.5, 1, false);
        assert.equal(media[0].preservesPitch, false);
        assert.throws(() => audio.configureTrack(id, 1, 0, 1.5, 1.5, false));
        audio.disposeTrack(id);
        assert.equal(media[0].paused, true);
        assert.doesNotThrow(() => audio.disposeTrack(id));
    } finally {
        audio.disposeAudio();
        delete globalThis.Audio; delete globalThis.AudioContext;
    }
});

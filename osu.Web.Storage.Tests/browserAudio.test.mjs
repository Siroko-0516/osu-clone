import assert from 'node:assert/strict';
import test from 'node:test';
import * as audio from '../osu.Web/wwwroot/js/browserAudio.mjs';

test('browser track clock, pause, seek-before-load, pitch modes, and disposal', async () => {
    const media = [];
    class FakeAudio extends EventTarget {
        constructor() { super(); this.readyState = 0; this.currentTime = 0; this.duration = NaN; this.paused = true; this.ended = false; this.abortNextPlay = false; media.push(this); }
        async play() {
            if (this.abortNextPlay) {
                this.abortNextPlay = false;
                throw new DOMException('metadata seek interrupted playback', 'AbortError');
            }
            this.paused = false;
        }
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
        audio.playTrack(id);
        await Promise.resolve(); await Promise.resolve();
        media[0].ended = true;
        media[0].paused = true;
        media[0].dispatchEvent(new Event('ended'));
        audio.unlockAudio();
        await Promise.resolve(); await Promise.resolve();
        assert.equal(media[0].paused, true, 'a later user gesture must not restart a completed track');
        audio.disposeTrack(id);
        assert.equal(media[0].paused, true);
        assert.doesNotThrow(() => audio.disposeTrack(id));
        assert.doesNotThrow(() => audio.stopTrack(id));
        assert.doesNotThrow(() => audio.playTrack(id));
        assert.doesNotThrow(() => audio.configureTrack(id, 1, 0, 1, 1, false));
        assert.equal(audio.seekTrack(id, 0), false);
        assert.deepEqual(audio.trackState(id), {duration:0, position:0, loaded:false, running:false, ended:true, error:''});

        const replacementId = audio.createTrack(new Uint8Array([4, 5, 6]));
        media[1].readyState = 4; media[1].duration = 20;
        media[1].dispatchEvent(new Event('loadedmetadata'));
        audio.playTrack(replacementId);
        await Promise.resolve();
        assert.equal(audio.trackState(replacementId).running, true);
        audio.disposeTrack(replacementId);

        const interruptedId = audio.createTrack(new Uint8Array([7, 8, 9]));
        media[2].readyState = 4; media[2].duration = 20; media[2].abortNextPlay = true;
        audio.playTrack(interruptedId);
        await Promise.resolve(); await Promise.resolve();
        assert.equal(audio.trackState(interruptedId).running, false);
        audio.configureTrack(interruptedId, 1, 0, 1, 1, false);
        await Promise.resolve(); await Promise.resolve();
        assert.equal(audio.trackState(interruptedId).running, true);
        audio.disposeTrack(interruptedId);
    } finally {
        audio.disposeAudio();
        delete globalThis.Audio; delete globalThis.AudioContext;
    }
});

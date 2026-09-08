import test from 'node:test';
import assert from 'node:assert/strict';
import * as samples from '../osu.Web/wwwroot/js/browserSamples.mjs';
import {disposeAudio} from '../osu.Web/wwwroot/js/browserAudio.mjs';

test('decoded samples share buffers, limit overlapping playback, and cancel pending starts', async () => {
    const sources = [];
    let finishDecode;
    const node = () => ({gain:{value:1},pan:{value:0},connect(other){return other;},disconnect(){}});
    globalThis.AudioContext = class {
        destination = {};
        decodeAudioData() {return new Promise(resolve => {finishDecode = resolve;});}
        createGain() {return node();}
        createStereoPanner() {return node();}
        createBufferSource() {
            const source = {...node(),playbackRate:{value:1},started:false,stopped:false,
                start(){this.started = true;},stop(){this.stopped = true;}};
            sources.push(source);
            return source;
        }
        async close() {}
    };
    const tick = async () => {await Promise.resolve(); await Promise.resolve(); await Promise.resolve();};
    try {
        const sample = samples.createSample(new Uint8Array([1]));
        const cancelled = samples.createChannel(sample);
        samples.playChannel(cancelled);
        assert.equal(samples.channelPlaying(cancelled), true, 'decoding must not make a queued playback get collected');
        samples.stopChannel(cancelled);
        const buffer = {duration:0.2};
        finishDecode(buffer);
        await tick();
        assert.equal(sources.length, 0, 'stopping during decode cancels playback');
        assert.deepEqual(samples.sampleState(sample), {loaded:true,duration:200,error:''});
        samples.configureSample(sample, 2);
        const first = samples.createChannel(sample);
        const second = samples.createChannel(sample);
        const third = samples.createChannel(sample);
        samples.configureChannel(first, .4, -.5, 1.5, 1, true);
        samples.playChannel(first); await tick();
        samples.playChannel(second); await tick();
        samples.playChannel(third); await tick();
        assert.equal(sources.length, 3);
        assert.ok(sources.every(source => source.buffer === buffer));
        assert.equal(sources[0].stopped, true);
        assert.equal(samples.channelPlaying(first), false);
        assert.equal(samples.channelPlaying(second), true);
        assert.equal(sources[0].playbackRate.value, 1.5);
        assert.equal(sources[0].loop, true);
        sources[1].onended();
        assert.equal(samples.channelPlaying(second), false);
        samples.disposeSample(sample);
        assert.equal(sources[2].stopped, true);
        assert.equal(samples.channelPlaying(third), false);
        assert.doesNotThrow(() => samples.disposeChannel(third));

        const delayed = samples.createSample(new Uint8Array([2]));
        const pending = samples.createChannel(delayed);
        samples.playChannel(pending);
        samples.disposeSample(delayed);
        finishDecode(buffer); await tick();
        assert.equal(sources.length, 3, 'disposing before decode finishes must not start audio');
    } finally {
        samples.disposeSamples();
        disposeAudio();
        delete globalThis.AudioContext;
    }
});

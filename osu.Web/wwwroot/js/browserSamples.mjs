import { getAudioContext } from './browserAudio.mjs';

const samples = new Map();
const channels = new Map();
let nextSample = 0, nextChannel = 0;

export function createSample(bytes) {
    const data = typeof bytes === 'string' ? Uint8Array.from(atob(bytes), c => c.charCodeAt(0)) : new Uint8Array(bytes);
    const sample = {buffer:null, error:'', disposed:false, active:[], concurrency:2};
    sample.ready = getAudioContext().decodeAudioData(data.buffer).then(buffer => {
        if (!sample.disposed) sample.buffer = buffer;
    }).catch(error => {sample.error = error.message;});
    samples.set(++nextSample, sample);
    return nextSample;
}

export function sampleState(id) {
    const sample = samples.get(id);
    return {loaded:!!sample.buffer, duration:(sample.buffer?.duration || 0) * 1000, error:sample.error};
}

export function configureSample(id, concurrency) {
    if (!Number.isInteger(concurrency) || concurrency < 0) throw new RangeError('Sample concurrency must be nonnegative.');
    samples.get(id).concurrency = concurrency;
}

export function createChannel(sampleId) {
    const ctx = getAudioContext();
    const gain = ctx.createGain(), pan = ctx.createStereoPanner();
    gain.connect(pan).connect(ctx.destination);
    channels.set(++nextChannel, {sample:samples.get(sampleId), gain, pan, source:null, requested:false, disposed:false, frequency:1, looping:false, generation:0});
    return nextChannel;
}

function stop(channel) {
    if (!channel) return;
    channel.generation++;
    channel.requested = false;
    if (channel.source) {
        channel.source.onended = null;
        channel.source.stop();
        channel.source.disconnect();
        channel.source = null;
    }
    const index = channel.sample.active.indexOf(channel);
    if (index !== -1) channel.sample.active.splice(index, 1);
}

export function playChannel(id) {
    const channel = channels.get(id);
    stop(channel);
    channel.requested = true;
    const generation = channel.generation;
    void channel.sample.ready.then(() => {
        const sample = channel.sample;
        if (channel.disposed || sample.disposed || !channel.requested || channel.generation !== generation) return;
        if (!sample.buffer || sample.concurrency === 0) {channel.requested = false; return;}
        while (sample.active.length >= sample.concurrency) stop(sample.active[0]);
        const source = getAudioContext().createBufferSource();
        source.buffer = sample.buffer;
        source.loop = channel.looping;
        source.playbackRate.value = channel.frequency;
        source.connect(channel.gain);
        source.onended = () => {if (channel.source === source) stop(channel);};
        channel.source = source;
        sample.active.push(channel);
        source.start();
    }).catch(error => {channel.sample.error = error.message; stop(channel);});
}

export function configureChannel(id, volume, balance, frequency, tempo, looping) {
    if (!Number.isFinite(frequency) || frequency <= 0 || tempo !== 1)
        throw new Error('Browser samples do not yet support reverse playback or independent tempo adjustment.');
    const channel = channels.get(id);
    channel.gain.gain.value = Math.max(0, volume);
    channel.pan.pan.value = Math.max(-1, Math.min(1, balance));
    channel.frequency = frequency;
    channel.looping = looping;
    if (channel.source) {channel.source.playbackRate.value = frequency; channel.source.loop = looping;}
}

export const channelPlaying = id => channels.get(id)?.requested || false;
export const stopChannel = id => stop(channels.get(id));

export function disposeChannel(id) {
    const channel = channels.get(id);
    if (!channel) return;
    stop(channel);
    channel.disposed = true;
    channel.gain.disconnect(); channel.pan.disconnect();
    channels.delete(id);
}

export function disposeSample(id) {
    const sample = samples.get(id);
    if (!sample) return;
    sample.disposed = true;
    for (const [channelId, channel] of channels) if (channel.sample === sample) disposeChannel(channelId);
    sample.buffer = null;
    samples.delete(id);
}

export function disposeSamples() {
    for (const id of samples.keys()) disposeSample(id);
}

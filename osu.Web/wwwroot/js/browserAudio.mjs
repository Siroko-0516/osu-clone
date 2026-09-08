const tracks = new Map();
let nextId = 0;
let context;
const getContext = () => context ??= new AudioContext({ latencyHint: 'interactive' });

export function createTrack(bytes) {
    const data = typeof bytes === 'string' ? Uint8Array.from(atob(bytes), c => c.charCodeAt(0)) : new Uint8Array(bytes);
    const url = URL.createObjectURL(new Blob([data]));
    const audio = new Audio(url);
    audio.preload = 'auto';
    const ctx = getContext();
    const gain = ctx.createGain();
    const pan = ctx.createStereoPanner();
    const source = ctx.createMediaElementSource(audio);
    source.connect(gain).connect(pan).connect(ctx.destination);
    const entry = {audio, url, gain, pan, source, requested:false, error:'', seekTo:null};
    audio.addEventListener('error', () => { entry.error = audio.error?.message || 'Audio decoding failed'; });
    audio.addEventListener('loadedmetadata', () => {
        if (entry.seekTo !== null) { audio.currentTime = Math.min(audio.duration, entry.seekTo); entry.seekTo = null; }
    });
    tracks.set(++nextId, entry);
    return nextId;
}

async function start(entry) {
    try {
        await getContext().resume();
        if (entry.requested) await entry.audio.play();
    } catch (error) {
        // Autoplay denial is retried only after an actual user gesture.
        if (error.name !== 'NotAllowedError' && error.name !== 'AbortError') entry.error = error.message;
    }
}

export function unlockAudio() {
    if (context) context.resume().catch(() => {});
    for (const entry of tracks.values()) if (entry.requested && entry.audio.paused) void start(entry);
}

export function playTrack(id) {
    const entry = tracks.get(id);
    entry.requested = true;
    void start(entry);
}

export function stopTrack(id) {
    const entry = tracks.get(id);
    entry.requested = false;
    entry.audio.pause();
}

export function seekTrack(id, milliseconds) {
    const entry = tracks.get(id);
    if (!Number.isFinite(milliseconds) || milliseconds < 0) return false;
    if (entry.audio.readyState === 0) { entry.seekTo = milliseconds / 1000; return true; }
    if (milliseconds > entry.audio.duration * 1000) return false;
    entry.audio.currentTime = milliseconds / 1000;
    return true;
}

export function configureTrack(id, volume, balance, frequency, tempo, looping) {
    const entry = tracks.get(id);
    if (frequency <= 0 || tempo <= 0 || (frequency !== 1 && tempo !== 1))
        throw new Error('Browser tracks do not yet support reverse playback or simultaneous independent pitch and tempo changes.');
    entry.gain.gain.value = Math.max(0, volume);
    entry.pan.pan.value = Math.max(-1, Math.min(1, balance));
    entry.audio.preservesPitch = frequency === 1;
    entry.audio.playbackRate = frequency * tempo;
    entry.audio.loop = looping;
}

export function trackState(id) {
    const entry = tracks.get(id);
    return {
        duration: Number.isFinite(entry.audio.duration) ? entry.audio.duration * 1000 : 0,
        position: (entry.seekTo ?? entry.audio.currentTime) * 1000,
        loaded: entry.audio.readyState >= 2,
        running: !entry.audio.paused && !entry.audio.ended && context?.state === 'running',
        ended: entry.audio.ended,
        error: entry.error
    };
}

export function disposeTrack(id) {
    const entry = tracks.get(id);
    if (!entry) return;
    entry.requested = false;
    entry.audio.pause();
    entry.audio.removeAttribute('src');
    entry.audio.load();
    entry.source.disconnect(); entry.gain.disconnect(); entry.pan.disconnect();
    URL.revokeObjectURL(entry.url);
    tracks.delete(id);
}

export function disposeAudio() {
    for (const id of tracks.keys()) disposeTrack(id);
    context?.close();
    context = undefined;
}

import { unlockAudio } from './browserAudio.mjs';

// Preserve input edges between animation frames, including taps shorter than one frame.
export function attachBrowserInput(canvas) {
    const events = [];
    const keys = new Set();
    const buttons = new Set();
    const remove = [];
    let pointerId = null;
    let x = 0, y = 0;
    const listen = (target, type, fn) => {
        target.addEventListener(type, fn, { passive: false });
        remove.push(() => target.removeEventListener(type, fn));
    };
    const position = event => {
        const rect = canvas.getBoundingClientRect();
        x = (event.clientX - rect.left) * canvas.width / Math.max(1, rect.width);
        y = (event.clientY - rect.top) * canvas.height / Math.max(1, rect.height);
    };
    const move = () => {
        const next = { kind: 'move', x, y };
        if (events.at(-1)?.kind === 'move') events[events.length - 1] = next;
        else events.push(next);
    };
    const releaseButtons = () => {
        for (const button of buttons) events.push({ kind: 'button', button, pressed: false, x, y });
        buttons.clear();
        pointerId = null;
    };
    const reset = () => {
        events.push({ kind: 'reset' });
        keys.clear();
        buttons.clear();
        pointerId = null;
    };
    listen(canvas, 'pointermove', event => {
        if (pointerId !== null && pointerId !== event.pointerId) return;
        position(event);
        move();
    });
    listen(canvas, 'pointerdown', event => {
        unlockAudio();
        if (pointerId !== null && pointerId !== event.pointerId) return;
        event.preventDefault();
        position(event);
        pointerId = event.pointerId;
        canvas.focus();
        canvas.setPointerCapture?.(pointerId);
        if (!buttons.has(event.button)) {
            buttons.add(event.button);
            events.push({ kind: 'button', button: event.button, pressed: true, x, y });
        }
    });
    listen(window, 'pointerup', event => {
        if (pointerId !== event.pointerId) return;
        position(event);
        if (buttons.delete(event.button)) events.push({ kind: 'button', button: event.button, pressed: false, x, y });
        if (!buttons.size) pointerId = null;
    });
    listen(canvas, 'lostpointercapture', releaseButtons);
    listen(window, 'pointercancel', event => { if (event.pointerId === pointerId) releaseButtons(); });
    listen(canvas, 'contextmenu', event => event.preventDefault());
    const isTextEntry = target => typeof target?.matches === 'function'
        && (target.matches('input, textarea, select') || target.isContentEditable);
    listen(window, 'keydown', event => {
        if (isTextEntry(event.target)) return;
        unlockAudio();
        if (!event.code || event.isComposing) return;
        event.preventDefault();
        if (event.repeat || keys.has(event.code)) return;
        keys.add(event.code);
        events.push({ kind: 'key', code: event.code, pressed: true });
    });
    listen(window, 'keyup', event => {
        if (keys.delete(event.code)) events.push({ kind: 'key', code: event.code, pressed: false });
    });
    listen(canvas, 'wheel', event => {
        event.preventDefault();
        const scale = event.deltaMode === 1 ? 1 : event.deltaMode === 2 ? 10 : 1 / 100;
        events.push({ kind: 'wheel', x: -event.deltaX * scale, y: -event.deltaY * scale });
    });
    listen(canvas, 'blur', reset);
    listen(window, 'blur', reset);
    listen(document, 'visibilitychange', () => { if (document.hidden) reset(); });
    return {
        drain: () => events.splice(0),
        hasHeldKeys: () => keys.size > 0,
        heldKeys: () => [...keys],
        reset,
        dispose() { for (const fn of remove) fn(); events.length = 0; keys.clear(); buttons.clear(); }
    };
}

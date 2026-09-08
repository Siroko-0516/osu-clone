import assert from 'node:assert/strict';
import test from 'node:test';
import { attachBrowserInput } from '../osu.Web/wwwroot/js/browserInput.mjs';
import { disposeAudio } from '../osu.Web/wwwroot/js/browserAudio.mjs';

test('input bridge preserves short taps, focus release, and scaled pointer coordinates', () => {
    globalThis.window = new EventTarget();
    globalThis.document = new EventTarget();
    let audioUnlocks = 0;
    globalThis.AudioContext = class { async resume() {audioUnlocks++;} async close() {} };
    const canvas = new EventTarget();
    canvas.width = 1280;
    canvas.height = 960;
    canvas.focus = () => {};
    canvas.getBoundingClientRect = () => ({left:10,top:20,width:640,height:480});
    const bridge = attachBrowserInput(canvas);
    const emit = (target, name, props) => target.dispatchEvent(Object.assign(new Event(name, {cancelable:true}), props));
    try {
        emit(canvas, 'keydown', {code:'KeyZ'});
        assert.equal(audioUnlocks, 1, 'the first user gesture unlocks audio before frame processing');
        emit(canvas, 'keydown', {code:'KeyZ',repeat:true});
        emit(window, 'keyup', {code:'KeyZ'});
        assert.deepEqual(bridge.drain(), [
            {kind:'key',code:'KeyZ',pressed:true},
            {kind:'key',code:'KeyZ',pressed:false}
        ]);
        emit(window, 'keydown', {code:'KeyX'});
        assert.deepEqual(bridge.drain(), [], 'keys outside the focused canvas are not captured');
        emit(canvas, 'pointermove', {clientX:20,clientY:30,pointerId:1});
        emit(canvas, 'pointermove', {clientX:110,clientY:120,pointerId:1});
        emit(canvas, 'pointerdown', {clientX:110,clientY:120,pointerId:1,button:0});
        emit(window, 'pointerup', {clientX:130,clientY:140,pointerId:1,button:0});
        assert.deepEqual(bridge.drain(), [
            {kind:'move',x:200,y:200},
            {kind:'button',button:0,pressed:true,x:200,y:200},
            {kind:'button',button:0,pressed:false,x:240,y:240}
        ]);
        emit(canvas, 'keydown', {code:'KeyD'});
        emit(canvas, 'keydown', {code:'KeyF'});
        emit(window, 'blur', {});
        const reset = bridge.drain();
        assert.equal(reset.length, 3);
        assert.equal(reset[2].kind, 'reset');
        emit(canvas, 'keydown', {code:'KeyD'});
        assert.equal(bridge.drain()[0].pressed, true, 'held keys are released on blur');
        emit(canvas, 'pointerdown', {clientX:10,clientY:20,pointerId:1,button:0});
        emit(window, 'pointercancel', {pointerId:1});
        assert.deepEqual(bridge.drain().map(x => x.pressed), [true,false]);
        bridge.dispose();
        emit(canvas, 'keydown', {code:'KeyX'});
        assert.deepEqual(bridge.drain(), []);
    } finally {
        bridge.dispose();
        disposeAudio();
        delete globalThis.AudioContext;
        delete globalThis.window;
        delete globalThis.document;
    }
});

# osu!lazer browser port

This branch is for a real browser port of the existing osu!lazer source tree. It is not the standalone JavaScript clone in the other repository.

## Current milestone

The framework diagnostic now runs in .NET WebAssembly, renders geometry, receives keyboard and pointer input, and uses browser-backed tracks and samples. Original `OsuGame` startup remains blocked by Realm's native `realm-wrappers` dependency. Original menus, beatmap selection, gameplay, and results are not running.

The default page runs `BrowserBootstrapGame`. Use **Attempt original OsuGame startup** (or `?original=true`) to run the real `OsuGame` startup path and report its first error. Switching runtimes reloads the page; changing a label is not evidence of successful game startup.

## Verified on 2026-09-08

- Restored the Blazor synchronization context around framework execution; frames advance continuously.
- Corrected texture-array interop and atlas upload coordinates; framework boxes and cursor render.
- Connected physical input edges, pointer scaling, and focus reset to a framework InputHandler.
- Routed TrackStore through browser audio: a five-second generated track completed, pause held at 360 ms, and seek moved to 2000 ms in an untrimmed browser build.
- Routed SampleStore through shared decoded buffers and independent channels: a 200 ms sample decoded and accepted four plays while frames continued without console errors.
- Added IndexedDB collection snapshots and atomic revision-checked metadata batches. These do not replace Realm-backed game services yet.
- Ten JavaScript tests pass, covering input, texture transport, audio lifecycle, collection migration, and transaction rollback. Run `npm ci --ignore-scripts && npm test` in `osu.Web.Storage.Tests`.

The browser renderer still inherits DummyRenderer and implements only a subset of geometry and texture operations. Full shaders, masks, blending, framebuffers, and other vertex formats remain unported. Audio mixer effects, reverse playback, independent sample tempo, and simultaneous independent track pitch/tempo are unsupported. Actual beatmap timing and latency remain unverified.

## Why the desktop project cannot be published directly

`osu.Desktop` targets a native window, native graphics backends, native audio, unrestricted filesystem storage, threads, and platform integrations. A browser supplies none of those interfaces directly. The .NET runtime can execute C# in WebAssembly, but the framework platform layer still needs browser implementations.

## Port layers

1. **Web host** — boot .NET on `browser-wasm`.
2. **Framework host** — implement a browser `GameHost` and single-thread scheduling mode.
3. **Graphics** — map osu!framework rendering to WebGL2 first, keeping WebGPU as a later option.
4. **Input** — translate Pointer Events, keyboard, wheel, touch, focus, and fullscreen APIs.
5. **Audio** — implement clock-stable Web Audio playback, samples, seeking, and latency compensation.
6. **Storage** — replace filesystem assumptions with OPFS/IndexedDB and browser file pickers.
7. **Game boot** — load `OsuGame`, then progressively enable rulesets, beatmap import, skins, and online APIs.
8. **Optimisation** — AOT, trimming annotations, asset streaming, worker/offscreen rendering experiments, and cache policy.

## Definition of the next milestone

The next milestone requires migrating the Realm-dependent game services to browser storage and reaching the original menu. Completion of the port additionally requires importing a beatmap, selecting it, playing with synchronized audio and judgement, reaching results, and restoring data after a browser restart. Verify each ruleset separately. A diagnostic scene or a successful build alone is not a completed game port.

## Legal

Keep the upstream MIT licence and copyright notice. The upstream README states separately that the MIT licence does not grant use of osu!/ppy branding and that game resources have their own licence.

## Historical compatibility result — Phase 1

The full `osu.Game` project graph compiles for `browser-wasm`, but publishing the linked game currently stops in the native WebAssembly step at an SDL callback:

```
The return type 'SDL.SDLBool' of pinvoke callback method
'SDL.SDLBool eventFilter(System.IntPtr, SDL.SDL_Event*)'
needs to be blittable.
```

This confirms the first concrete platform boundary: the packaged osu!framework pulls its native SDL host into the browser publish. The next change must happen in an osu-framework fork, where a browser host can exclude SDL and supply canvas, input, audio, and storage adapters. This cannot be correctly solved by hiding the error in the game project.

## Historical browser boot fix

The browser framework fork now recognises `OperatingSystem.IsBrowser()` as a dedicated runtime platform. The web integration build tracks framework commit `6b24601`, removing the `RuntimeInfo` type-initialisation failure and substituting a browser-safe no-output mixer for native BASS during early boot.

The audio thread also skips native BASS CPU statistics in WebAssembly. That initial no-output stage has now been extended with the track and sample providers described above.


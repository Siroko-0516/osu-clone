# osu!lazer browser port

This branch is for a real browser port of the existing osu!lazer source tree. It is not the standalone JavaScript clone in the other repository.

## Current milestone

The browser shell runs in .NET WebAssembly with song import/selection, original ruleset gameplay sessions, pause/restart, results and locally saved scores. It uses browser-backed tracks, samples and IndexedDB. The original `OsuGame` menus and results are not ported; native Realm dependencies are bypassed by the browser shell. See the verification sections below: compiled integration and browser playthrough coverage are distinct.

The default page runs `BrowserBootstrapGame`. Open **Diagnostics** (`?diagnostics=true`) for framework probes. The legacy `?original=true` option selects `BrowserOsuGame`, which currently inherits the same bootstrap shell; it does not run the original desktop `OsuGame`. Switching runtimes reloads the page.

## Historical verification on 2026-09-08

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


## Memory and gameplay lifecycle update (2026-10-22)

- Browser frame and vertex arrays now reuse geometric capacity and shrink after a substantially smaller scene. An explicit zero live-vertex count clears stale geometry even when the transport buffer retains capacity.
- Host shutdown deletes textures, buffers, programs and linked shaders, releases CPU arrays, and invalidates callbacks from an old host generation.
- Selected beatmap/audio copies in the WASM filesystem are deleted once their streams have been consumed; the IndexedDB originals remain available for replay and preview.
- Score and health processors now update inside the original ruleset's frame-stable component container. The browser shell displays score, accuracy and maximum combo after completion.
- Custom tracks are registered with the framework audio loop so their queued disposal actually releases media resources. Session removal disposes its drawable tree before replacement.
- Play/restart creates a fresh attempt. Preview uses a separate track, so preview seeking cannot alter gameplay judgements. Song/mode transitions detach the previous session.
- The file-store tests are included in the npm test command again and use the current on-demand read API.

Validation: 13 JavaScript tests pass, including 100 fluctuating frame transfers, peak-buffer reclamation, empty-frame handling, CPU/GPU teardown and stale callbacks. These are mocked WebGL tests, not an end-to-end browser memory benchmark. The Release WebAssembly host build passes using direct MSBuild invocation (the SDK CLI itself fails with `Process.GetStat`/`get_StartTimeCore` in this environment). All 10 managed checks pass, including original ruleset settings, archive import, persistence, and four-ruleset beatmap conversion. Actual browser playthroughs remain unverified: installing the headless browser failed with a truncated download. PR builds run without publishing to GitHub Pages.

This update does not complete the original-client port. Full renderer shader/mask/framebuffer support, the original menu/results screens, and audio-timing verification for all four rulesets remain open. The result panel added here belongs to the browser shell.

## Browser player flow (2026-10-22)

- Responsive song library with search, 20-set pagination, separate per-difficulty audio previews and settings for the four modes.
- Dedicated gameplay screen, live score/accuracy/combo HUD, Esc or focus-loss pause, resume, fresh restart, fullscreen and touch controls. Menu navigation keeps normal keyboard behaviour.
- Completed attempts are saved through the existing IndexedDB score collection and restored into the recent-score list on reload. A storage failure preserves the visible result and reports the failure.
- Standard alpha blending is enabled for browser quads. This does not implement the framework's full blend states, masks, shaders or framebuffer pipeline.
- HUD refresh is isolated to its own component at 5 Hz and stops refreshing while paused. Song pages and temporary file hydration remain bounded.

Validation: the Release managed WebAssembly build and all 24 JavaScript/managed checks pass locally, including failed score saves, retry, and independent replay persistence. The web CI now installs Chromium and runs generated, self-contained four-mode maps through import, pause/resume, completion, score save and reload, with screenshots uploaded even on failure. These playthrough checks deliberately miss the notes; they do not establish input timing accuracy or visual parity with the desktop client.

# osu!lazer browser port

This branch is for a real browser port of the existing osu!lazer source tree. It is not the standalone JavaScript clone in the other repository.

## Current milestone

Phase 0 establishes a deployable .NET WebAssembly host alongside the unchanged game projects. It deliberately does not claim that osu!framework runs in-browser yet.

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

The next milestone is complete only when an osu!framework test scene renders its first frame inside a browser canvas. A loading page alone is not considered a game port.

## Legal

Keep the upstream MIT licence and copyright notice. The upstream README states separately that the MIT licence does not grant use of osu!/ppy branding and that game resources have their own licence.

## Compatibility result — Phase 1

The full `osu.Game` project graph compiles for `browser-wasm`, but publishing the linked game currently stops in the native WebAssembly step at an SDL callback:

```
The return type 'SDL.SDLBool' of pinvoke callback method
'SDL.SDLBool eventFilter(System.IntPtr, SDL.SDL_Event*)'
needs to be blittable.
```

This confirms the first concrete platform boundary: the packaged osu!framework pulls its native SDL host into the browser publish. The next change must happen in an osu-framework fork, where a browser host can exclude SDL and supply canvas, input, audio, and storage adapters. This cannot be correctly solved by hiding the error in the game project.

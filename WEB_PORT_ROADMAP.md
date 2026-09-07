# osu!lazer browser port roadmap

The goal is to run the original osu!lazer client offline in a browser while retaining the built-in osu!, mania, taiko and catch rulesets.

## Milestones

- [ ] 1. Browser-controlled game lifecycle and frame pump
- [ ] 2. osu.Framework WebGL renderer
- [ ] 3. Browser input handlers
- [ ] 4. Web Audio backend and timing
- [ ] 5. IndexedDB-backed storage
- [ ] 6. .osz file import and beatmap registration
- [ ] 7. Replace or isolate desktop-native dependencies
- [ ] 8. Boot original menu, song select, gameplay and results screens
- [ ] 9. Verify all four built-in rulesets
- [ ] 10. Reduce download size and memory usage

## Completed foundations

- [x] Original osu.Game compiles for browser-wasm.
- [x] All four built-in ruleset assemblies compile and ship in the deployed runtime.
- [x] WebGL2 diagnostic surface.
- [x] JavaScript-to-.NET input bridge.
- [x] GitHub Pages deployment from web-port.
- [x] Initial BrowserGameHost single-frame pump entry point.

Online login, score submission and multiplayer remain out of scope until offline play works.

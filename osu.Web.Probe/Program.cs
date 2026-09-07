// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Game;

// This executable is a compatibility probe, not the final host.
// Reaching this line means the complete osu.Game dependency graph linked for browser-wasm.
Console.WriteLine(typeof(OsuGame).FullName);

// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using osu.Framework.Input.Bindings;

namespace osu.Game.Input.Bindings
{
    /// <summary>Committed key mappings independent of a native database and its live objects.</summary>
    public interface IKeyBindingSource
    {
        IReadOnlyList<IKeyBinding> GetBindings(string? ruleset, int? variant);
        IDisposable Subscribe(string? ruleset, int? variant, Action changed);
    }
}

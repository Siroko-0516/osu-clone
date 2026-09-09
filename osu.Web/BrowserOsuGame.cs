// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Game.Database.Persistence;
using osu.Game.Input.Bindings;

namespace osu.Web
{
    /// <summary>
    /// Browser game entry point. Browser services are added here without loading Realm,
    /// whose native wrapper cannot execute in WebAssembly.
    /// </summary>
    public sealed class BrowserOsuGame : BrowserBootstrapGame
    {
        public BrowserOsuGame(IKeyBindingSource bindingSource, IGamePersistence persistence)
            : base(bindingSource, persistence)
        {
        }
    }
}

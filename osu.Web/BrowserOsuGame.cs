// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Game;
using osu.Game.Database.Persistence;

namespace osu.Web
{
    /// <summary>
    /// Original osu! game entry point with browser-owned persistence.
    /// </summary>
    public sealed class BrowserOsuGame : OsuGame
    {
        private readonly IGamePersistence persistence;

        protected override IGamePersistence GamePersistence => persistence;

        public BrowserOsuGame(IGamePersistence persistence)
        {
            this.persistence = persistence;
        }
    }
}

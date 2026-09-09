// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable enable

using System.Threading.Tasks;
using osu.Game.Configuration;

namespace osu.Game.Database.Persistence
{
    /// <summary>
    /// Default game persistence implementation backed by an <see cref="IRecordStore"/>.
    /// </summary>
    public sealed class GamePersistence : IGamePersistence
    {
        public IRecordStore Records { get; }

        public SettingsStore RulesetSettings { get; }

        public BeatmapCatalogStore Beatmaps { get; }

        public GameCollectionStore<SkinSnapshot> Skins { get; }

        public GameCollectionStore<ScoreSnapshot> Scores { get; }

        private GamePersistence(
            IRecordStore records,
            SettingsStore rulesetSettings,
            BeatmapCatalogStore beatmaps,
            GameCollectionStore<SkinSnapshot> skins,
            GameCollectionStore<ScoreSnapshot> scores)
        {
            Records = records;
            RulesetSettings = rulesetSettings;
            Beatmaps = beatmaps;
            Skins = skins;
            Scores = scores;
        }

        public static async Task<GamePersistence> CreateAsync(IRecordStore records, SettingsStore rulesetSettings)
        {
            var beatmaps = await BeatmapCatalogStore.CreateAsync(records).ConfigureAwait(false);
            var skins = await GameCollectionStore<SkinSnapshot>.CreateAsync(records, "skins").ConfigureAwait(false);
            var scores = await GameCollectionStore<ScoreSnapshot>.CreateAsync(records, "scores").ConfigureAwait(false);
            return new GamePersistence(records, rulesetSettings, beatmaps, skins, scores);
        }
    }
}

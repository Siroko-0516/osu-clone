// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable enable

using System.Linq;
using System.Threading.Tasks;
using osu.Game.Configuration;
using osu.Game.Skinning;

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
            await ensureBuiltInSkinsPresent(skins).ConfigureAwait(false);
            var scores = await GameCollectionStore<ScoreSnapshot>.CreateAsync(records, "scores").ConfigureAwait(false);
            return new GamePersistence(records, rulesetSettings, beatmaps, skins, scores);
        }

        private static async Task ensureBuiltInSkinsPresent(GameCollectionStore<SkinSnapshot> skins)
        {
            var builtInSkins = new[]
            {
                new SkinSnapshot(SkinInfo.ARGON_SKIN.ToString(), "osu! argon", string.Empty, true),
                new SkinSnapshot(SkinInfo.ARGON_PRO_SKIN.ToString(), "osu! argon pro", string.Empty, true),
                new SkinSnapshot(SkinInfo.TRIANGLES_SKIN.ToString(), "osu!triangles", string.Empty, true),
                new SkinSnapshot(SkinInfo.CLASSIC_SKIN.ToString(), "osu! default", string.Empty, true),
                new SkinSnapshot(SkinInfo.RETRO_SKIN.ToString(), "osu!classic", string.Empty, true),
            };

            foreach (SkinSnapshot skin in builtInSkins)
            {
                if (skins.Values.Any(existing => existing.Id == skin.Id))
                    continue;

                await skins.SaveAsync(skin.Id, skin).ConfigureAwait(false);
            }
        }
    }
}

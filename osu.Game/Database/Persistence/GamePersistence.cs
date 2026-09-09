// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable enable

using System.Threading.Tasks;

namespace osu.Game.Database.Persistence
{
    /// <summary>
    /// Default game persistence implementation backed by an <see cref="IRecordStore"/>.
    /// </summary>
    public sealed class GamePersistence : IGamePersistence
    {
        public IRecordStore Records { get; }

        public BeatmapCatalogStore Beatmaps { get; }

        private GamePersistence(IRecordStore records, BeatmapCatalogStore beatmaps)
        {
            Records = records;
            Beatmaps = beatmaps;
        }

        public static async Task<GamePersistence> CreateAsync(IRecordStore records)
        {
            var beatmaps = await BeatmapCatalogStore.CreateAsync(records).ConfigureAwait(false);
            return new GamePersistence(records, beatmaps);
        }
    }
}

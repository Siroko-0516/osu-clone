// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace osu.Game.Database.Persistence
{
    /// <summary>
    /// Database-independent beatmap catalogue used by platforms which cannot load Realm.
    /// Binary beatmap resources are stored separately and referenced by relative path.
    /// </summary>
    public sealed class BeatmapCatalogStore
    {
        public const string SET_COLLECTION = "beatmapSets";
        public const string BEATMAP_COLLECTION = "beatmaps";

        private const int schema_version = 1;
        private readonly IRecordStore records;
        private readonly Dictionary<string, CatalogEntry<BeatmapSetSnapshot>> sets = new(StringComparer.Ordinal);
        private readonly Dictionary<string, CatalogEntry<BeatmapSnapshot>> beatmaps = new(StringComparer.Ordinal);

        private static readonly JsonSerializerOptions json_options = new(JsonSerializerDefaults.Web);

        private BeatmapCatalogStore(IRecordStore records) => this.records = records;

        public IReadOnlyList<BeatmapSetSnapshot> Sets => sets.Values.Select(entry => entry.Value)
                                                                  .OrderBy(set => set.Artist, StringComparer.OrdinalIgnoreCase)
                                                                  .ThenBy(set => set.Title, StringComparer.OrdinalIgnoreCase)
                                                                  .ToArray();

        public IReadOnlyList<BeatmapSnapshot> Beatmaps => beatmaps.Values.Select(entry => entry.Value).ToArray();

        public static async Task<BeatmapCatalogStore> CreateAsync(IRecordStore records)
        {
            var result = new BeatmapCatalogStore(records);
            await result.hydrateAsync().ConfigureAwait(false);
            return result;
        }

        public IReadOnlyList<BeatmapSnapshot> GetBeatmaps(string setId) => beatmaps.Values
            .Select(entry => entry.Value)
            .Where(beatmap => beatmap.SetId == setId)
            .OrderBy(beatmap => beatmap.RulesetId)
            .ThenBy(beatmap => beatmap.StarRating)
            .ToArray();

        /// <summary>
        /// Commits the set and every supplied difficulty atomically. In-memory state changes only after commit.
        /// </summary>
        public async Task SaveSetAsync(BeatmapSetSnapshot set, IReadOnlyList<BeatmapSnapshot> difficulties)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(set.Id);
            if (difficulties.Any(beatmap => beatmap.SetId != set.Id))
                throw new ArgumentException("Every beatmap must reference the imported set.", nameof(difficulties));
            if (difficulties.Select(beatmap => beatmap.Id).Distinct(StringComparer.Ordinal).Count() != difficulties.Count)
                throw new ArgumentException("A beatmap set cannot contain duplicate difficulty IDs.", nameof(difficulties));

            var changes = new List<RecordChange>(difficulties.Count + 1)
            {
                createChange(SET_COLLECTION, set.Id, set, sets.GetValueOrDefault(set.Id)?.Revision ?? 0)
            };

            changes.AddRange(difficulties.Select(beatmap =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(beatmap.Id);
                return createChange(BEATMAP_COLLECTION, beatmap.Id, beatmap, beatmaps.GetValueOrDefault(beatmap.Id)?.Revision ?? 0);
            }));

            StoredRecord[] committed = await records.SaveBatchAsync(changes).ConfigureAwait(false);
            sets[set.Id] = new CatalogEntry<BeatmapSetSnapshot>(set, committed[0].Revision);
            for (int i = 0; i < difficulties.Count; i++)
                beatmaps[difficulties[i].Id] = new CatalogEntry<BeatmapSnapshot>(difficulties[i], committed[i + 1].Revision);
        }

        private async Task hydrateAsync()
        {
            foreach (StoredRecord record in await records.ListAsync(SET_COLLECTION).ConfigureAwait(false))
            {
                var value = JsonSerializer.Deserialize<BeatmapSetSnapshot>(record.Payload, json_options)
                            ?? throw new InvalidOperationException($"Stored beatmap set '{record.Id}' is empty.");
                sets[record.Id] = new CatalogEntry<BeatmapSetSnapshot>(value, record.Revision);
            }

            foreach (StoredRecord record in await records.ListAsync(BEATMAP_COLLECTION).ConfigureAwait(false))
            {
                var value = JsonSerializer.Deserialize<BeatmapSnapshot>(record.Payload, json_options)
                            ?? throw new InvalidOperationException($"Stored beatmap '{record.Id}' is empty.");
                if (sets.ContainsKey(value.SetId))
                    beatmaps[record.Id] = new CatalogEntry<BeatmapSnapshot>(value, record.Revision);
            }
        }

        private static RecordChange createChange<T>(string collection, string id, T value, long revision) =>
            new RecordChange(collection, id, JsonSerializer.Serialize(value, json_options), schema_version, revision);

        private sealed record CatalogEntry<T>(T Value, long Revision);
    }

    public sealed record BeatmapSetSnapshot(string Id, string Artist, string Title, string Creator, string ResourcePath);

    public sealed record BeatmapSnapshot(string Id, string SetId, string DifficultyName, int RulesetId, double StarRating, string BeatmapPath, string AudioPath);
}


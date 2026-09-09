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
    /// Database-independent collection of immutable metadata snapshots.
    /// </summary>
    public sealed class GameCollectionStore<T>
        where T : class
    {
        private const int schema_version = 1;

        private static readonly JsonSerializerOptions json_options = new(JsonSerializerDefaults.Web);

        private readonly IRecordStore records;
        private readonly string collection;
        private readonly Dictionary<string, Entry> entries = new(StringComparer.Ordinal);

        private GameCollectionStore(IRecordStore records, string collection)
        {
            this.records = records;
            this.collection = collection;
        }

        public IReadOnlyList<T> Values => entries.Values.Select(entry => entry.Value).ToArray();

        public static async Task<GameCollectionStore<T>> CreateAsync(IRecordStore records, string collection)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(collection);

            var result = new GameCollectionStore<T>(records, collection);
            foreach (StoredRecord record in await records.ListAsync(collection).ConfigureAwait(false))
            {
                var value = JsonSerializer.Deserialize<T>(record.Payload, json_options)
                            ?? throw new InvalidOperationException($"Stored {collection} record '{record.Id}' is empty.");
                result.entries[record.Id] = new Entry(value, record.Revision);
            }

            return result;
        }

        public async Task SaveAsync(string id, T value)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(id);
            ArgumentNullException.ThrowIfNull(value);

            long revision;
            if (entries.TryGetValue(id, out Entry? entry))
            {
                revision = entry.Revision;
            }
            else
            {
                StoredRecord? existing = await records.GetAsync(collection, id).ConfigureAwait(false);
                revision = existing?.Revision ?? 0;

                // Deleted records remain in the backing store so they can be restored. A new save
                // must first acknowledge that revision and clear the deletion marker.
                if (existing?.DeletePending == true)
                {
                    StoredRecord? restored = await records.SetDeletePendingAsync(collection, id, false, revision).ConfigureAwait(false);
                    revision = restored?.Revision
                               ?? throw new InvalidOperationException($"Stored {collection} record '{id}' disappeared while restoring it.");
                }
            }

            StoredRecord committed = await records.SaveAsync(
                collection,
                id,
                JsonSerializer.Serialize(value, json_options),
                schema_version,
                revision).ConfigureAwait(false);

            entries[id] = new Entry(value, committed.Revision);
        }

        public async Task<bool> DeleteAsync(string id)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(id);

            if (!entries.TryGetValue(id, out Entry? entry))
                return false;

            StoredRecord? committed = await records.SetDeletePendingAsync(collection, id, true, entry.Revision).ConfigureAwait(false);
            if (committed is null)
                return false;

            entries.Remove(id);
            return true;
        }

        private sealed record Entry(T Value, long Revision);
    }
}

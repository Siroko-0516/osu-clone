// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Text.Json;
using osu.Game.Configuration;
using osu.Game.Database.Persistence;

namespace osu.Web.Storage;

/// <summary>Hydrated synchronous settings for original ruleset config managers, with explicit asynchronous commit.</summary>
public sealed class BrowserSettingsStore : SettingsStore
{
    private const string collection = "ruleset-settings";
    private readonly IRecordStore records;
    private readonly Dictionary<string, Entry> entries = new();
    private readonly SemaphoreSlim flushLock = new(1, 1);

    private sealed class Entry
    {
        public Dictionary<string, string> Values = new();
        public long Revision;
        public long Generation;
        public long SavedGeneration;
    }

    private BrowserSettingsStore(IRecordStore records) => this.records = records;

    public static async Task<BrowserSettingsStore> CreateAsync(IRecordStore records)
    {
        var result = new BrowserSettingsStore(records);
        foreach (var record in await records.ListAsync(collection, includeDeletePending: true))
        {
            if (record.SchemaVersion != 1 || record.DeletePending)
                throw new InvalidDataException("Ruleset settings need migration or restoration before use.");
            var values = JsonSerializer.Deserialize<Dictionary<string, string>>(record.Payload)
                         ?? throw new InvalidDataException("Invalid ruleset settings payload.");
            if (values.Any(pair => pair.Value is null)) throw new InvalidDataException("Null ruleset setting value.");
            result.entries.Add(record.Id, new Entry { Values = values, Revision = record.Revision });
        }
        return result;
    }

    private static string key(string ruleset, int variant) => Uri.EscapeDataString(ruleset) + ":" + variant.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public override Dictionary<string, string> ReadSettings(string ruleset, int variant)
    {
        lock (entries)
            return entries.TryGetValue(key(ruleset, variant), out var entry) ? new(entry.Values) : new();
    }

    public override void WriteSettings(string ruleset, int variant, IReadOnlyDictionary<string, string> values)
    {
        lock (entries)
        {
            string id = key(ruleset, variant);
            if (!entries.TryGetValue(id, out var entry)) entries.Add(id, entry = new Entry());
            foreach (var pair in values)
            {
                if (entry.Values.TryGetValue(pair.Key, out var current) && current == pair.Value) continue;
                entry.Values[pair.Key] = pair.Value;
                entry.Generation++;
            }
        }
    }

    public async Task FlushAsync()
    {
        await flushLock.WaitAsync();
        try
        {
            RecordChange[] changes;
            Dictionary<string, long> generations;
            lock (entries)
            {
                var dirty = entries.Where(pair => pair.Value.Generation != pair.Value.SavedGeneration).ToArray();
                changes = dirty.Select(pair => new RecordChange(collection, pair.Key, JsonSerializer.Serialize(pair.Value.Values), 1, pair.Value.Revision)).ToArray();
                generations = dirty.ToDictionary(pair => pair.Key, pair => pair.Value.Generation);
            }
            if (changes.Length == 0) return;
            // A failed commit leaves every dirty generation available for retry.
            var committed = await records.SaveBatchAsync(changes);
            lock (entries)
                foreach (var record in committed)
                {
                    entries[record.Id].Revision = record.Revision;
                    entries[record.Id].SavedGeneration = generations[record.Id];
                }
        }
        finally { flushLock.Release(); }
    }
}

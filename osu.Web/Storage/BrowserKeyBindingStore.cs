// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Text.Json;
using osu.Framework.Input.Bindings;
using osu.Game.Database.Persistence;
using osu.Game.Input.Bindings;

namespace osu.Web.Storage;

public sealed class BrowserKeyBindingStore : IKeyBindingSource
{
    private const string collection = "key-bindings";
    private readonly IRecordStore records;
    private readonly Dictionary<string, (Binding[] Bindings, long Revision)> mappings = new();
    private readonly Dictionary<string, List<Action>> subscriptions = new();
    private readonly SemaphoreSlim writeLock = new(1, 1);
    public sealed record Binding(int Action, int[] Keys);

    private BrowserKeyBindingStore(IRecordStore records) => this.records = records;

    public static async Task<BrowserKeyBindingStore> CreateAsync(IRecordStore records)
    {
        var result = new BrowserKeyBindingStore(records);
        foreach (var record in await records.ListAsync(collection, includeDeletePending: true))
        {
            if (record.SchemaVersion != 1 || record.DeletePending) throw new InvalidDataException("Key bindings need migration or restoration.");
            var bindings = JsonSerializer.Deserialize<Binding[]>(record.Payload) ?? throw new InvalidDataException("Invalid key binding payload.");
            validate(bindings);
            result.mappings.Add(record.Id, (bindings, record.Revision));
        }
        return result;
    }

    private static string key(string? ruleset, int? variant) =>
        (ruleset is null ? "global" : "ruleset:" + Uri.EscapeDataString(ruleset)) + ":" + (variant?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "global");

    private static void validate(Binding[] bindings)
    {
        foreach (var binding in bindings)
            if (binding is null || binding.Keys is null || binding.Keys.Any(value => !Enum.IsDefined(typeof(InputKey), value)))
                throw new InvalidDataException("Unknown input key in stored binding.");
    }

    public IReadOnlyList<IKeyBinding> GetBindings(string? ruleset, int? variant)
    {
        if (!mappings.TryGetValue(key(ruleset, variant), out var entry)) return Array.Empty<IKeyBinding>();
        // Consumers filter and edit mappings; never expose the persisted snapshot itself.
        return entry.Bindings.Select(binding => (IKeyBinding)new KeyBinding(new KeyCombination(binding.Keys.Select(value => (InputKey)value).ToArray()), binding.Action)).ToArray();
    }

    public IDisposable Subscribe(string? ruleset, int? variant, Action changed)
    {
        string id = key(ruleset, variant);
        if (!subscriptions.TryGetValue(id, out var callbacks)) subscriptions.Add(id, callbacks = new());
        callbacks.Add(changed);
        return new Subscription(() => callbacks.Remove(changed));
    }

    public async Task SaveBindingsAsync(string? ruleset, int? variant, IEnumerable<IKeyBinding> bindings)
    {
        string id = key(ruleset, variant);
        var snapshot = bindings.Select(binding => new Binding(Convert.ToInt32(binding.Action), binding.KeyCombination.Keys.Select(input => (int)input).ToArray())).ToArray();
        validate(snapshot);
        await writeLock.WaitAsync();
        try
        {
            long revision = mappings.TryGetValue(id, out var previous) ? previous.Revision : 0;
            var committed = await records.SaveBatchAsync(new[] { new RecordChange(collection, id, JsonSerializer.Serialize(snapshot), 1, revision) });
            mappings[id] = (snapshot, committed[0].Revision);
            if (subscriptions.TryGetValue(id, out var callbacks))
                foreach (var callback in callbacks.ToArray()) callback();
        }
        finally { writeLock.Release(); }
    }

    private sealed class Subscription(Action dispose) : IDisposable
    {
        private Action? release = dispose;
        public void Dispose() => Interlocked.Exchange(ref release, null)?.Invoke();
    }
}

using System.Text.Json;
using osu.Game.Database.Persistence;
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.Osu.Configuration;
using osu.Web.Storage;

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

var records = new MemoryRecords();
var settings = await BrowserSettingsStore.CreateAsync(records);
using var config = new OsuRulesetConfigManager(settings, new OsuRuleset().RulesetInfo);
config.SetValue(OsuRulesetSetting.SnakingInSliders, false);
config.Save();
await settings.FlushAsync();
var restored = await BrowserSettingsStore.CreateAsync(records);
using var restoredConfig = new OsuRulesetConfigManager(restored, new OsuRuleset().RulesetInfo);
Check(!restoredConfig.Get<bool>(OsuRulesetSetting.SnakingInSliders), "Original config failed to restore the persisted value.");
Console.WriteLine("PASS original ruleset config round trip without Realm");

settings.WriteSettings("osu", 1, new Dictionary<string, string> { ["independent"] = "variant" });
records.FailNext = true;
try { await settings.FlushAsync(); throw new Exception("Expected commit failure."); }
catch (IOException) { }
Check(!records.Items.ContainsKey("osu:1"), "Failed commit changed persisted state.");
await settings.FlushAsync();
Check(records.Items.ContainsKey("osu:1"), "Failed commit discarded pending values.");
Check(!settings.ReadSettings("osu", 0).ContainsKey("independent"), "Variant settings leaked.");
Console.WriteLine("PASS commit retry and variant isolation");

settings.WriteSettings("osu", 1, new Dictionary<string, string> { ["independent"] = "first" });
records.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var flush = settings.FlushAsync();
await records.Started.Task;
settings.WriteSettings("osu", 1, new Dictionary<string, string> { ["independent"] = "second" });
records.Gate.SetResult();
await flush;
records.Gate = null;
await settings.FlushAsync();
Check(JsonSerializer.Deserialize<Dictionary<string, string>>(records.Items["osu:1"].Payload)!["independent"] == "second", "In-flight commit lost a later setting change.");
Console.WriteLine("PASS changes during commit remain dirty");

var stale = await BrowserSettingsStore.CreateAsync(records);
settings.WriteSettings("osu", 1, new Dictionary<string, string> { ["independent"] = "newer tab" });
await settings.FlushAsync();
stale.WriteSettings("osu", 1, new Dictionary<string, string> { ["independent"] = "stale tab" });
try { await stale.FlushAsync(); throw new Exception("Expected revision conflict."); }
catch (IOException) { }
Check(JsonSerializer.Deserialize<Dictionary<string, string>>(records.Items["osu:1"].Payload)!["independent"] == "newer tab", "Stale tab overwrote committed settings.");
Console.WriteLine("PASS stale writers cannot overwrite settings");

sealed class MemoryRecords : IRecordStore
{
    public readonly Dictionary<string, StoredRecord> Items = new();
    public bool FailNext;
    public TaskCompletionSource? Gate;
    public readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task<StoredRecord?> GetAsync(string collection, string id) => Task.FromResult(Items.GetValueOrDefault(id));
    public Task<StoredRecord[]> ListAsync(string collection, bool includeDeletePending = false) => Task.FromResult(Items.Values.Where(r => r.Collection == collection).ToArray());
    public async Task<StoredRecord[]> SaveBatchAsync(IReadOnlyList<RecordChange> changes)
    {
        if (Gate is not null) { Started.TrySetResult(); await Gate.Task; }
        if (FailNext) { FailNext = false; throw new IOException("Simulated storage quota failure."); }
        foreach (var change in changes)
            if ((Items.GetValueOrDefault(change.Id)?.Revision ?? 0) != change.ExpectedRevision) throw new IOException("Revision conflict.");
        var committed = changes.Select(c => new StoredRecord(c.Collection, c.Id, c.Payload, c.SchemaVersion, c.ExpectedRevision + 1, false)).ToArray();
        foreach (var item in committed) Items[item.Id] = item;
        return committed;
    }
    public Task<StoredRecord> SaveAsync(string collection, string id, string payload, int schemaVersion, long expectedRevision) => throw new NotSupportedException();
    public Task<StoredRecord?> SetDeletePendingAsync(string collection, string id, bool value, long expectedRevision) => throw new NotSupportedException();
}

using System.Text.Json;
using osu.Game.Database.Persistence;
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.Osu.Configuration;
using osu.Game.Rulesets.Mania;
using osu.Web.Storage;
using osu.Framework.Input.Bindings;

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

var keys = await BrowserKeyBindingStore.CreateAsync(records);
int notifications = 0;
using var subscription = keys.Subscribe("osu", 0, () => notifications++);
var mappings = new IKeyBinding[] { new KeyBinding(InputKey.A, OsuAction.LeftButton), new KeyBinding(InputKey.X, OsuAction.RightButton) };
records.FailNext = true;
try { await keys.SaveBindingsAsync("osu", 0, mappings); throw new Exception("Expected keymap commit failure."); }
catch (IOException) { }
Check(notifications == 0 && keys.GetBindings("osu", 0).Count == 0, "Failed keymap save changed active mappings.");
await keys.SaveBindingsAsync("osu", 0, mappings);
Check(notifications == 1, "Committed keymap change did not notify the input container.");
keys.GetBindings("osu", 0)[0].KeyCombination = new KeyCombination(InputKey.None);
Check(keys.GetBindings("osu", 0)[0].KeyCombination.Keys.Contains(InputKey.A), "Consumer mutation corrupted the stored keymap.");
var restoredKeys = await BrowserKeyBindingStore.CreateAsync(records);
Check(restoredKeys.GetBindings("osu", 0)[0].KeyCombination.Keys.Contains(InputKey.A), "Keymap did not restore.");
Check(restoredKeys.GetBindings("osu", 1).Count == 0, "Keymap leaked between variants.");
subscription.Dispose();
await keys.SaveBindingsAsync("osu", 0, mappings);
Check(notifications == 1, "Disposed subscriber still received keymap changes.");
Console.WriteLine("PASS keymap persistence, commit notifications, snapshot isolation and unsubscription");

var mania = new ManiaRuleset();
for (int keyCount = 1; keyCount <= 9; keyCount++)
{
    var defaults = mania.GetDefaultKeyBindings(keyCount).Where(binding => !binding.KeyCombination.Keys.Contains(InputKey.None)).ToArray();
    Check(defaults.Length == keyCount, $"mania {keyCount}K did not expose one physical binding per lane.");
    Check(defaults.Select(binding => Convert.ToInt32(binding.Action)).Distinct().Count() == keyCount, $"mania {keyCount}K lane actions were not unique.");
}
await restoredKeys.SaveBindingsAsync(ManiaRuleset.SHORT_NAME, 4, mania.GetDefaultKeyBindings(4).Where(binding => !binding.KeyCombination.Keys.Contains(InputKey.None)));
await restoredKeys.SaveBindingsAsync(ManiaRuleset.SHORT_NAME, 9, mania.GetDefaultKeyBindings(9).Where(binding => !binding.KeyCombination.Keys.Contains(InputKey.None)));
Check(restoredKeys.GetBindings(ManiaRuleset.SHORT_NAME, 4).Count == 4, "mania 4K bindings were not stored by variant.");
Check(restoredKeys.GetBindings(ManiaRuleset.SHORT_NAME, 9).Count == 9, "mania 9K bindings were not stored by variant.");
Console.WriteLine("PASS original mania 1K through 9K defaults and variant-specific persistence");

using var mappingProbe = new MappingProbe();
var filtered = mappingProbe.Apply(new IKeyBinding[]
{
    new KeyBinding(InputKey.MouseWheelUp, OsuAction.LeftButton),
    new KeyBinding(InputKey.Z, OsuAction.LeftButton),
    new KeyBinding(InputKey.Z, OsuAction.RightButton),
});
Check(filtered.Length == 2 && filtered.All(binding => binding.KeyCombination.Keys.Contains(InputKey.None)), "Original gameplay mapping safety rules were bypassed.");
Console.WriteLine("PASS original gameplay duplicate and wheel-binding filters");

sealed class MappingProbe : osu.Game.Rulesets.UI.RulesetInputManager<OsuAction>.RulesetKeyBindingContainer
{
    public MappingProbe() : base(new OsuRuleset().RulesetInfo, 0, SimultaneousBindingMode.Unique) { }
    public IKeyBinding[] Apply(IEnumerable<IKeyBinding> bindings)
    {
        ReloadMappings(bindings);
        return KeyBindings.ToArray();
    }
}

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

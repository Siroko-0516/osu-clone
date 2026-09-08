// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Text.Json;
using osu.Game.Database.Persistence;
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.Osu.Configuration;
using osu.Game.Rulesets.Mania;
using osu.Web.Storage;
using osu.Framework.Input.Bindings;
using System.IO.Compression;

static class Program
{
    public static async Task Main()
    {
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
        Check(!records.Items.ContainsKey(MemoryRecords.Key("ruleset-settings", "osu:1")), "Failed commit changed persisted state.");
        await settings.FlushAsync();
        Check(records.Items.ContainsKey(MemoryRecords.Key("ruleset-settings", "osu:1")), "Failed commit discarded pending values.");
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
        Check(JsonSerializer.Deserialize<Dictionary<string, string>>(records.Items[MemoryRecords.Key("ruleset-settings", "osu:1")].Payload)!["independent"] == "second", "In-flight commit lost a later setting change.");
        Console.WriteLine("PASS changes during commit remain dirty");
        
        var stale = await BrowserSettingsStore.CreateAsync(records);
        settings.WriteSettings("osu", 1, new Dictionary<string, string> { ["independent"] = "newer tab" });
        await settings.FlushAsync();
        stale.WriteSettings("osu", 1, new Dictionary<string, string> { ["independent"] = "stale tab" });
        try { await stale.FlushAsync(); throw new Exception("Expected revision conflict."); }
        catch (IOException) { }
        Check(JsonSerializer.Deserialize<Dictionary<string, string>>(records.Items[MemoryRecords.Key("ruleset-settings", "osu:1")].Payload)!["independent"] == "newer tab", "Stale tab overwrote committed settings.");
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
        
        var catalogueRecords = new MemoryRecords();
        var catalogue = await BeatmapCatalogStore.CreateAsync(catalogueRecords);
        var set = new BeatmapSetSnapshot("set-1", "artist", "title", "mapper", "imports/set-1");
        var difficulties = new[]
        {
            new BeatmapSnapshot("map-1", set.Id, "Normal", 0, 2.1, "imports/set-1/normal.osu", "imports/set-1/audio.mp3"),
            new BeatmapSnapshot("map-2", set.Id, "4K Hard", 3, 3.8, "imports/set-1/4k-hard.osu", "imports/set-1/audio.mp3")
        };
        await catalogue.SaveSetAsync(set, difficulties);
        var restoredCatalogue = await BeatmapCatalogStore.CreateAsync(catalogueRecords);
        Check(restoredCatalogue.Sets.Single() == set, "Beatmap set metadata did not survive restart.");
        Check(restoredCatalogue.GetBeatmaps(set.Id).Select(map => map.Id).ToHashSet().SetEquals(new[] { "map-1", "map-2" }), "Beatmap difficulties did not survive restart.");
        catalogueRecords.FailNext = true;
        try
        {
            await restoredCatalogue.SaveSetAsync(set with { Title = "Must roll back" }, new[] { difficulties[0] with { DifficultyName = "Must roll back" } });
            throw new Exception("Expected catalogue commit failure.");
        }
        catch (IOException) { }
        Check(restoredCatalogue.Sets.Single().Title == "title" && restoredCatalogue.GetBeatmaps(set.Id).Single(map => map.Id == "map-1").DifficultyName == "Normal",
            "Failed atomic beatmap import changed the active catalogue.");
        Console.WriteLine("PASS browser beatmap catalogue atomic import and restart hydration");

        using var osz = new MemoryStream();
        using (var zip = new ZipArchive(osz, ZipArchiveMode.Create, leaveOpen: true))
        {
            var map = zip.CreateEntry("Artist - Title (Mapper) [Hard].osu");
            using (var writer = new StreamWriter(map.Open()))
                writer.Write("osu file format v14\n[General]\nAudioFilename: audio.mp3\nMode: 3\n[Metadata]\nTitle:Title\nArtist:Artist\nCreator:Mapper\nVersion:Hard\nBeatmapID:456\nBeatmapSetID:123\n");
            var audio = zip.CreateEntry("audio.mp3");
            using (var audioStream = audio.Open())
                audioStream.Write(new byte[] { 1, 2, 3 });
        }
        osz.Position = 0;
        var imported = OszArchiveReader.Read(osz);
        Check(imported.Set.Id == "123" && imported.Set.Title == "Title", ".osz set metadata was not parsed.");
        Check(imported.Beatmaps.Single().Id == "456" && imported.Beatmaps.Single().RulesetId == 3, ".osz difficulty metadata was not parsed.");
        Check(imported.Files.All(file => file.Path.StartsWith("beatmaps/123/", StringComparison.Ordinal)), ".osz resources escaped the set directory.");
        using var unsafeOsz = new MemoryStream();
        using (var zip = new ZipArchive(unsafeOsz, ZipArchiveMode.Create, leaveOpen: true))
            zip.CreateEntry("../escape.osu");
        unsafeOsz.Position = 0;
        try { OszArchiveReader.Read(unsafeOsz); throw new Exception("Expected unsafe archive path rejection."); }
        catch (InvalidDataException) { }
        Console.WriteLine("PASS .osz metadata parsing, resource namespacing and traversal rejection");
    }
}


// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.IO.Compression;
using osu.Game.Database.Persistence;
using osu.Game.Rulesets.Mania.Beatmaps;
using osu.Game.Rulesets.Mania.Objects;
using osu.Web.Storage;

internal static class BeatmapLoadingChecks
{
    public static async Task Run()
    {
        var records = new MemoryRecords();
        var catalogue = await BeatmapCatalogStore.CreateAsync(records).ConfigureAwait(false);
        var files = new Dictionary<string, byte[]>();
        for (int mode = 0; mode < 4; mode++)
        {
            using var archive = new MemoryStream();
            using (var zip = new ZipArchive(archive, ZipArchiveMode.Create, leaveOpen: true))
            {
                using (var writer = new StreamWriter(zip.CreateEntry("map.osu").Open()))
                {
                    writer.Write($"osu file format v14\n[General]\nAudioFilename: audio.wav\nPreviewTime: 1500\nMode: {mode}\n[Metadata]\nTitle:Test {mode}\nArtist:Artist\nCreator:Mapper\nVersion:Hard\nBeatmapSetID:{mode + 100}\nBeatmapID:{mode + 1000}\n[Difficulty]\nCircleSize:7\nOverallDifficulty:5\nHPDrainRate:5\nSliderMultiplier:1.4\nSliderTickRate:1\n[TimingPoints]\n0,500,4,2,1,60,1,0\n[HitObjects]\n64,192,1000,1,0,0:0:0:0:\n");
                    if (mode == 3) writer.Write("448,192,2000,128,0,3000:0:0:0:0:\n");
                }
                using var audio = zip.CreateEntry("Audio.wav").Open();
                audio.Write(new byte[] { 1, 2, 3 });
            }
            archive.Position = 0;
            var package = OszArchiveReader.Read(archive);
            if (!package.Beatmaps.Single().AudioPath.EndsWith("/Audio.wav", StringComparison.Ordinal))
                throw new InvalidOperationException("Audio references were not resolved to the stored filename's casing.");
            foreach (var file in package.Files) files[file.Path] = file.Data;
            await catalogue.SaveSetAsync(package.Set, package.Beatmaps).ConfigureAwait(false);
        }

        // Recreate the catalogue as on a browser reload, then use the real decoder and converters.
        catalogue = await BeatmapCatalogStore.CreateAsync(records).ConfigureAwait(false);
        using var loader = new BrowserBeatmapLoader(catalogue, path => files.TryGetValue(path, out var bytes)
            ? new MemoryStream(bytes, writable: false) : throw new FileNotFoundException(path));
        for (int mode = 0; mode < 4; mode++)
        {
            var beatmap = loader.Load((mode + 1000).ToString());
            if (beatmap.BeatmapInfo.Ruleset.OnlineID != mode || beatmap.HitObjects.Count == 0 || beatmap.Metadata.PreviewTime != 1500)
                throw new InvalidOperationException($"Mode {mode} did not decode after restoring the catalogue.");
            if (mode == 3 && (beatmap is not ManiaBeatmap mania || mania.TotalColumns != 7
                             || mania.HitObjects.OfType<HoldNote>().Single().EndTime != 3000))
                throw new InvalidOperationException("mania columns or hold duration changed during conversion.");
        }

        try { loader.Load("missing"); throw new InvalidOperationException("Missing difficulty was accepted."); }
        catch (InvalidDataException) { }
        files.Remove("beatmaps/100/Audio.wav");
        try { loader.Load("1000"); throw new InvalidOperationException("Missing audio was accepted."); }
        catch (FileNotFoundException) { }
        var original = catalogue.Beatmaps.Single(map => map.Id == "1001");
        await catalogue.SaveSetAsync(catalogue.Sets.Single(set => set.Id == original.SetId), new[] { original with { BeatmapPath = "../escape.osu" } }).ConfigureAwait(false);
        try { loader.Load(original.Id); throw new InvalidOperationException("Unsafe stored path was accepted."); }
        catch (InvalidDataException) { }
        using var duplicates = new MemoryStream();
        using (var zip = new ZipArchive(duplicates, ZipArchiveMode.Create, leaveOpen: true))
        {
            zip.CreateEntry("Audio.wav");
            zip.CreateEntry("audio.wav");
        }
        duplicates.Position = 0;
        try { OszArchiveReader.Read(duplicates); throw new InvalidOperationException("Ambiguous archive resource paths were accepted."); }
        catch (InvalidDataException) { }
        Console.WriteLine("PASS restored song selection, four original ruleset conversions, mania holds, missing files and path validation");
    }
}

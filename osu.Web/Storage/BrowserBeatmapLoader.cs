// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Formats;
using osu.Game.Database.Persistence;
using osu.Game.IO;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.Taiko;
using osu.Game.Rulesets.Catch;
using osu.Game.Rulesets.Mania;

namespace osu.Web.Storage;

/// <summary>Loads imported difficulties through the original decoder and gameplay conversion pipeline.</summary>
public sealed class BrowserBeatmapLoader : IDisposable
{
    private readonly BeatmapCatalogStore catalogue;
    private readonly Func<string, Stream> openFile;
    private readonly AssemblyRulesetStore rulesets;

    public BrowserBeatmapLoader(BeatmapCatalogStore catalogue, Func<string, Stream> openFile)
    {
        this.catalogue = catalogue;
        this.openFile = openFile;
        // Load the built-in assemblies before discovering them; no disk or Realm discovery.
        _ = new Ruleset[] { new OsuRuleset(), new TaikoRuleset(), new CatchRuleset(), new ManiaRuleset() };
        rulesets = new AssemblyRulesetStore(discoverFromDisk: false);
    }

    public IBeatmap Load(string difficultyId)
    {
        BeatmapSnapshot difficulty = catalogue.Beatmaps.SingleOrDefault(map => map.Id == difficultyId)
                                     ?? throw new InvalidDataException("The selected difficulty is no longer in the library.");
        BeatmapSetSnapshot set = catalogue.Sets.Single(item => item.Id == difficulty.SetId);
        ValidateResourcePath(set.ResourcePath, difficulty.BeatmapPath);
        ValidateResourcePath(set.ResourcePath, difficulty.AudioPath);
        using (openFile(difficulty.AudioPath)) { }
        using Stream stream = openFile(difficulty.BeatmapPath);
        using var reader = new LineBufferedReader(stream);
        Decoder.RegisterDependencies(rulesets);
        Beatmap decoded = Decoder.GetDecoder<Beatmap>(reader).Decode(reader);
        if (decoded.BeatmapInfo.Ruleset.OnlineID != difficulty.RulesetId)
            throw new InvalidDataException("The stored mode does not match the difficulty file. Import the beatmap again.");

        var working = new FlatWorkingBeatmap(decoded);
        return working.GetPlayableBeatmap(decoded.BeatmapInfo.Ruleset);
    }

    private static void ValidateResourcePath(string root, string path)
    {
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(path)
            || path.Contains('\\') || path.Contains(':') || path.StartsWith('/')
            || path.Split('/').Any(segment => segment is "" or "." or "..")
            || !path.StartsWith(root.TrimEnd('/') + "/", StringComparison.Ordinal))
            throw new InvalidDataException("The selected beatmap contains an invalid resource path.");
    }

    public void Dispose() => rulesets.Dispose();
}

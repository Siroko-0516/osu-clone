// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Game.Configuration;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Configuration;

namespace osu.Web;

/// <summary>
/// Creates ruleset configuration managers from the browser settings snapshot without Realm.
/// </summary>
public sealed class BrowserRulesetConfigCache(SettingsStore settings) : IRulesetConfigCache, IDisposable
{
    private readonly Dictionary<string, IRulesetConfigManager?> configs = new(StringComparer.Ordinal);

    public IRulesetConfigManager? GetConfigFor(Ruleset ruleset)
    {
        string name = ruleset.RulesetInfo.ShortName
                      ?? throw new InvalidOperationException("A ruleset name is required.");
        if (!configs.TryGetValue(name, out IRulesetConfigManager? config))
            configs[name] = config = ruleset.CreateConfig(settings);
        return config;
    }

    public void Dispose()
    {
        foreach (IRulesetConfigManager? config in configs.Values)
            config?.Dispose();
        configs.Clear();
    }
}

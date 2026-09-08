// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Game.Database;
using System.Collections.Generic;
using System.Linq;

namespace osu.Game.Configuration
{
    public class SettingsStore
    {
        // this class mostly exists as a wrapper to avoid breaking the ruleset API (see usage in RulesetConfigManager).
        // it may cease to exist going forward, depending on how the structure of the config data layer changes.

        public readonly RealmAccess Realm;

        public SettingsStore(RealmAccess realm)
        {
            Realm = realm;
        }

        protected SettingsStore()
        {
            Realm = null!;
        }

        public virtual Dictionary<string, string> ReadSettings(string ruleset, int variant) =>
            Realm == null ? new Dictionary<string, string>() : Realm.Realm.All<RealmRulesetSetting>()
                .Where(setting => setting.RulesetName == ruleset && setting.Variant == variant)
                .ToDictionary(setting => setting.Key, setting => setting.Value);

        public virtual void WriteSettings(string ruleset, int variant, IReadOnlyDictionary<string, string> values)
        {
            Realm?.Write(realm =>
            {
                foreach (var pair in values)
                {
                    var setting = realm.All<RealmRulesetSetting>().FirstOrDefault(s => s.RulesetName == ruleset && s.Variant == variant && s.Key == pair.Key);
                    if (setting == null)
                        realm.Add(new RealmRulesetSetting { RulesetName = ruleset, Variant = variant, Key = pair.Key, Value = pair.Value });
                    else
                        setting.Value = pair.Value;
                }
            });
        }
    }
}

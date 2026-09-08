// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using osu.Framework.Bindables;
using osu.Framework.Configuration;
using osu.Framework.Extensions;
using osu.Game.Configuration;

namespace osu.Game.Rulesets.Configuration
{
    public abstract class RulesetConfigManager<TLookup> : ConfigManager<TLookup>, IRulesetConfigManager
        where TLookup : struct, Enum
    {
        private readonly SettingsStore store;

        private readonly int variant;

        private Dictionary<string, string> databasedSettings = new Dictionary<string, string>();

        private readonly string rulesetName;

        protected RulesetConfigManager(SettingsStore store, RulesetInfo ruleset, int? variant = null)
        {
            this.store = store;

            rulesetName = ruleset.ShortName;

            this.variant = variant ?? 0;

            Load();

            InitialiseDefaults();
        }

        protected override void PerformLoad()
        {
            databasedSettings = store?.ReadSettings(rulesetName, variant) ?? new Dictionary<string, string>();
        }

        private readonly HashSet<TLookup> pendingWrites = new HashSet<TLookup>();

        protected override bool PerformSave()
        {
            TLookup[] changed;

            lock (pendingWrites)
            {
                changed = pendingWrites.ToArray();
                pendingWrites.Clear();
            }

            if (!changed.Any())
                return true;

            try
            {
                store?.WriteSettings(rulesetName, variant, changed.ToDictionary(c => c.ToString(), c => ConfigStore[c].ToString(CultureInfo.InvariantCulture)));
            }
            catch
            {
                lock (pendingWrites)
                    pendingWrites.UnionWith(changed);
                throw;
            }

            return true;
        }

        protected override void AddBindable<TBindable>(TLookup lookup, Bindable<TBindable> bindable)
        {
            base.AddBindable(lookup, bindable);

            if (databasedSettings.TryGetValue(lookup.ToString(), out var value))
            {
                bindable.Parse(value, CultureInfo.InvariantCulture);
            }
            else
            {
                value = bindable.ToString(CultureInfo.InvariantCulture);
                store?.WriteSettings(rulesetName, variant, new Dictionary<string, string> { [lookup.ToString()] = value });
                databasedSettings.Add(lookup.ToString(), value);
            }

            bindable.ValueChanged += _ =>
            {
                lock (pendingWrites)
                    pendingWrites.Add(lookup);
            };
        }
    }
}

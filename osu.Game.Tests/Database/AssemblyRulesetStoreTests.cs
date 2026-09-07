// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using NUnit.Framework;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Catch;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.Taiko;

namespace osu.Game.Tests.Database
{
    // Deliberately does not derive from RealmTest: registration must work without opening a database.
    [TestFixture]
    public class AssemblyRulesetStoreTests
    {
        [Test]
        public void LoadedBuiltInRulesetsCanBeResolvedWithoutDiskDiscovery()
        {
            Ruleset[] expected = { new OsuRuleset(), new TaikoRuleset(), new CatchRuleset(), new ManiaRuleset() };

            using var store = new AssemblyRulesetStore(discoverFromDisk: false);

            foreach (var ruleset in expected)
            {
                var actual = store.GetRuleset(ruleset.RulesetInfo.OnlineID);
                Assert.That(actual, Is.Not.Null);
                Assert.That(actual!.IsManaged, Is.False);
                Assert.That(actual.Available, Is.True);
                Assert.That(actual.CreateInstance(), Is.TypeOf(ruleset.GetType()));
                Assert.That(store.GetRuleset(actual.ShortName), Is.SameAs(actual));
            }
        }
    }
}

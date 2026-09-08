// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Input.Bindings;
using osu.Game.Rulesets.Osu;

sealed class MappingProbe : osu.Game.Rulesets.UI.RulesetInputManager<OsuAction>.RulesetKeyBindingContainer
{
    public MappingProbe()
        : base(new OsuRuleset().RulesetInfo, 0, SimultaneousBindingMode.Unique)
    {
    }

    public IKeyBinding[] Apply(IEnumerable<IKeyBinding> bindings)
    {
        ReloadMappings(bindings);
        return KeyBindings.ToArray();
    }
}


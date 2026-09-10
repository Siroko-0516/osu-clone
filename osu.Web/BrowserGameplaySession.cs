// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Audio.Track;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Beatmaps;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.UI;
using osu.Game.Rulesets.UI.Scrolling;
using osu.Game.Screens.Play;
using osu.Game.Skinning;

namespace osu.Web;

/// <summary>
/// Owns one browser gameplay attempt and supplies the same core dependency
/// boundary that the desktop Player normally provides.
/// </summary>
public sealed partial class BrowserGameplaySession : CompositeDrawable
{
    public IBeatmap Beatmap { get; }
    public DrawableRuleset DrawableRuleset { get; }
    public GameplayClockContainer Clock { get; }
    public Track Track { get; }
    public ScoreProcessor ScoreProcessor { get; }
    public HealthProcessor HealthProcessor { get; }
    public GameplayState GameplayState { get; }

    public double StartTime => DrawableRuleset.GameplayStartTime;

    public bool IsReady => DrawableRuleset.IsLoaded && DrawableRuleset.Playfield.IsLoaded;

    public BrowserGameplaySession(IBeatmap beatmap, Ruleset ruleset, Track track, ISkin rulesetSkin)
    {
        ArgumentNullException.ThrowIfNull(beatmap);
        ArgumentNullException.ThrowIfNull(ruleset);
        ArgumentNullException.ThrowIfNull(track);
        ArgumentNullException.ThrowIfNull(rulesetSkin);

        RelativeSizeAxes = Axes.Both;
        Beatmap = beatmap;
        Track = track;

        ScoreProcessor = ruleset.CreateScoreProcessor();
        ScoreProcessor.ApplyBeatmap(beatmap);

        HealthProcessor = ruleset.CreateHealthProcessor(beatmap.HitObjects[0].StartTime);
        HealthProcessor.ApplyBeatmap(beatmap);

        GameplayState = new GameplayState(
            beatmap,
            ruleset,
            scoreProcessor: ScoreProcessor,
            healthProcessor: HealthProcessor);

        DrawableRuleset = ruleset.CreateDrawableRulesetWith(beatmap);
        Clock = new GameplayClockContainer(track, applyOffsets: false, requireDecoupling: true)
        {
            Child = new SkinProvidingContainer(rulesetSkin)
            {
                Child = DrawableRuleset,
            },
        };

        // Match Player's pause contract so the ruleset input manager and the
        // gameplay clock cannot disagree after pausing, resuming or replacing a map.
        ((IBindable<bool>)DrawableRuleset.IsPaused).BindTo(Clock.IsPaused);

        InternalChild = Clock;
    }

    protected override IReadOnlyDependencyContainer CreateChildDependencies(IReadOnlyDependencyContainer parent)
    {
        var dependencies = new DependencyContainer(base.CreateChildDependencies(parent));
        dependencies.CacheAs(DrawableRuleset);
        dependencies.CacheAs(ScoreProcessor);
        dependencies.CacheAs(HealthProcessor);
        dependencies.CacheAs(GameplayState);
        dependencies.CacheAs<IGameplayClock>(Clock);

        if (DrawableRuleset is IDrawableScrollingRuleset scrolling)
            dependencies.CacheAs(scrolling.ScrollingInfo);

        return dependencies;
    }

    public void Start() => Clock.Reset(StartTime, startClock: true);
    public void Pause() => Clock.Stop();
    public void Resume() => Clock.Start();
    public void Restart() => Clock.Reset(StartTime, startClock: true);
    public void Seek(double time) => Clock.Seek(Math.Max(0, time));

    protected override void Dispose(bool isDisposing)
    {
        if (isDisposing)
        {
            Clock.Stop();
            Track.Dispose();
        }

        base.Dispose(isDisposing);
    }
}

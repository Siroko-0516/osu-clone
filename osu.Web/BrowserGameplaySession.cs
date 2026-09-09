// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Audio.Track;
using osu.Game.Beatmaps;
using osu.Game.Rulesets;
using osu.Game.Rulesets.UI;
using osu.Game.Screens.Play;

namespace osu.Web;

/// <summary>Owns one browser gameplay attempt and keeps audio, clock and playfield together.</summary>
public sealed class BrowserGameplaySession : IDisposable
{
    public IBeatmap Beatmap { get; }
    public DrawableRuleset DrawableRuleset { get; }
    public GameplayClockContainer Clock { get; }
    public Track Track { get; }

    public BrowserGameplaySession(IBeatmap beatmap, Ruleset ruleset, Track track)
    {
        ArgumentNullException.ThrowIfNull(beatmap);
        ArgumentNullException.ThrowIfNull(ruleset);
        ArgumentNullException.ThrowIfNull(track);

        Beatmap = beatmap;
        Track = track;
        DrawableRuleset = ruleset.CreateDrawableRulesetWith(beatmap);
        Clock = new GameplayClockContainer(track, applyOffsets: false, requireDecoupling: true)
        {
            Child = DrawableRuleset,
        };
    }

    public void Start() => Clock.Reset(0, startClock: true);
    public void Pause() => Clock.Stop();
    public void Resume() => Clock.Start();
    public void Restart() => Clock.Reset(0, startClock: true);
    public void Seek(double time) => Clock.Seek(Math.Max(0, time));

    public void Dispose()
    {
        Clock.Stop();
        Clock.Expire();
        Track.Dispose();
    }
}

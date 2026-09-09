// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable enable

using System;

namespace osu.Game.Database.Persistence
{
    public sealed record ScoreSnapshot(
        string Id,
        string BeatmapId,
        int RulesetId,
        long TotalScore,
        double Accuracy,
        DateTimeOffset Date,
        string PlayerName);
}

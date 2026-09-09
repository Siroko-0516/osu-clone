// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable enable

namespace osu.Game.Database.Persistence
{
    /// <summary>
    /// Platform-neutral entry point for persistent game data.
    ///
    /// Consumers must depend on this contract rather than Realm, IndexedDB or a UI host.
    /// Additional stores (skins, scores and collections) can be added here as they are
    /// migrated without leaking platform-specific storage into game components.
    /// </summary>
    public interface IGamePersistence
    {
        IRecordStore Records { get; }

        BeatmapCatalogStore Beatmaps { get; }

        GameCollectionStore<SkinSnapshot> Skins { get; }

        GameCollectionStore<ScoreSnapshot> Scores { get; }
    }
}

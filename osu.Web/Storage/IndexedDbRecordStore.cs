// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using Microsoft.JSInterop;
using osu.Game.Database.Persistence;

namespace osu.Web.Storage;

/// <summary>
/// Browser metadata adapter. This must be hydrated asynchronously before game services use its snapshots.
/// Do not synchronously block the framework update thread waiting for IndexedDB.
/// </summary>
public sealed class IndexedDbRecordStore : IRecordStore, IAsyncDisposable
{
    private readonly Lazy<Task<IJSObjectReference>> module;

    public IndexedDbRecordStore(IJSRuntime js)
    {
        module = new Lazy<Task<IJSObjectReference>>(() =>
            js.InvokeAsync<IJSObjectReference>("import", "./js/recordStore.mjs").AsTask());
    }

    public async Task<StoredRecord?> GetAsync(string collection, string id) =>
        await (await module.Value).InvokeAsync<StoredRecord?>("get", collection, id);

    public async Task<StoredRecord> SaveAsync(string collection, string id, string payload, int schemaVersion, long expectedRevision) =>
        await (await module.Value).InvokeAsync<StoredRecord>("save", collection, id, payload, schemaVersion, expectedRevision);

    public async Task<StoredRecord?> SetDeletePendingAsync(string collection, string id, bool value, long expectedRevision) =>
        await (await module.Value).InvokeAsync<StoredRecord?>("setDeletePending", collection, id, value, expectedRevision);

    public async ValueTask DisposeAsync()
    {
        if (module.IsValueCreated && module.Value.IsCompletedSuccessfully)
            await (await module.Value).DisposeAsync();
    }
}

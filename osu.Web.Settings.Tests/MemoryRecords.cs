// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Game.Database.Persistence;

sealed class MemoryRecords : IRecordStore
{
    public readonly Dictionary<string, StoredRecord> Items = new();
    public bool FailNext;
    public TaskCompletionSource? Gate;
    public readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public static string Key(string collection, string id) => collection + "\0" + id;

    public Task<StoredRecord?> GetAsync(string collection, string id) => Task.FromResult(Items.GetValueOrDefault(Key(collection, id)));

    public Task<StoredRecord[]> ListAsync(string collection, bool includeDeletePending = false) =>
        Task.FromResult(Items.Values.Where(r => r.Collection == collection && (includeDeletePending || !r.DeletePending)).ToArray());

    public async Task<StoredRecord[]> SaveBatchAsync(IReadOnlyList<RecordChange> changes)
    {
        if (Gate is not null)
        {
            Started.TrySetResult();
            await Gate.Task;
        }

        if (FailNext)
        {
            FailNext = false;
            throw new IOException("Simulated storage quota failure.");
        }

        foreach (var change in changes)
        {
            if ((Items.GetValueOrDefault(Key(change.Collection, change.Id))?.Revision ?? 0) != change.ExpectedRevision)
                throw new IOException("Revision conflict.");
        }

        var committed = changes.Select(c => new StoredRecord(c.Collection, c.Id, c.Payload, c.SchemaVersion, c.ExpectedRevision + 1, false)).ToArray();
        foreach (var item in committed)
            Items[Key(item.Collection, item.Id)] = item;

        return committed;
    }

    public Task<StoredRecord> SaveAsync(string collection, string id, string payload, int schemaVersion, long expectedRevision) => throw new NotSupportedException();

    public Task<StoredRecord?> SetDeletePendingAsync(string collection, string id, bool value, long expectedRevision) => throw new NotSupportedException();
}


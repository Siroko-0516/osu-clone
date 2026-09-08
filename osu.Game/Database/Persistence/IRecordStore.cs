// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable enable

using System.Threading.Tasks;
using System.Collections.Generic;

namespace osu.Game.Database.Persistence
{
    /// <summary>
    /// Asynchronous, versioned metadata storage. Payloads are JSON snapshots, never live database objects.
    /// Large binary assets are deliberately excluded from this contract.
    /// </summary>
    public interface IRecordStore
    {
        Task<StoredRecord?> GetAsync(string collection, string id);

        /// <summary>Reads a committed collection snapshot for asynchronous startup hydration.</summary>
        Task<StoredRecord[]> ListAsync(string collection, bool includeDeletePending = false);

        /// <summary>Saves all changes in one transaction. A conflict must roll back the entire batch.</summary>
        Task<StoredRecord[]> SaveBatchAsync(IReadOnlyList<RecordChange> changes);

        /// <summary>
        /// Atomically saves a snapshot if its revision matches. Use revision zero only for creation.
        /// Failure must leave the previously committed record intact.
        /// </summary>
        Task<StoredRecord> SaveAsync(string collection, string id, string payload, int schemaVersion, long expectedRevision);

        /// <summary>
        /// Atomically changes reversible deletion state. Returns null for a missing record at revision zero.
        /// </summary>
        Task<StoredRecord?> SetDeletePendingAsync(string collection, string id, bool value, long expectedRevision);
    }

    public sealed record StoredRecord(string Collection, string Id, string Payload, int SchemaVersion, long Revision, bool DeletePending);
    public sealed record RecordChange(string Collection, string Id, string Payload, int SchemaVersion, long ExpectedRevision);
}

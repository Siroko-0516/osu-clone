# Browser metadata storage

Run `npm ci --ignore-scripts` and `npm test` in this directory.
The tests use fake-indexeddb, not browser persistence or quota simulation.

## Current implementation

`IRecordStore` exchanges JSON snapshots and contains no Realm types.
`IndexedDbRecordStore` registers with the web host but is not yet consumed by
`OsuGameBase`, `SkinManager`, or `ModelManager`. The Realm boot failure remains.

Records are keyed by collection and ID. Revision zero creates a record; updates
require its current revision. Lookup and mutation share one read/write transaction.
Completion is reported after commit. Deleted records retain their payload.

The IndexedDB database schema is version 1. Payload schema versions are stored
separately. Downgrades are rejected. No automatic payload migration, backup/export,
binary file storage, pagination, or cross-tab change notification is implemented.
Unknown payload versions must not be interpreted by consumers without a migration.

## Integration boundary

Existing game managers use synchronous Realm transactions and managed models.
Do not wrap IndexedDB calls with blocking waits to preserve that API. Load detached
snapshots asynchronously before starting dependent game services. Extract model
mapping and query requirements before wiring a manager to this adapter.
The earlier synchronous deletion interface is only an extraction of existing Realm
behavior; it is not the browser persistence API.

## Remaining verification

Compile the C# adapter; exercise it through Blazor in a real browser; verify reload,
quota denial, blocked upgrades, private browsing behavior, and multi-tab lifecycle.
Passing these metadata tests is not evidence of original game boot or rendering.

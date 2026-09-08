import { test } from "node:test";
import assert from "node:assert/strict";
import { IDBFactory } from "fake-indexeddb";
import { createRecordStore } from "../osu.Web/wwwroot/js/recordStore.mjs";

test("collection index upgrade preserves version-one data and filters soft deletion", async () => {
    const factory = new IDBFactory();
    await new Promise((resolve, reject) => {
        const request = factory.open("upgrade", 1);
        request.onupgradeneeded = () => {
            const records = request.result.createObjectStore("records", {keyPath:["collection", "id"]});
            records.put({collection:"skins",id:"legacy",payload:'{"name":"Existing"}',schemaVersion:1,revision:1,deletePending:false});
        };
        request.onerror = () => reject(request.error);
        request.onsuccess = () => {request.result.close(); resolve();};
    });
    const store = createRecordStore(factory, "upgrade");
    assert.equal((await store.list("skins"))[0].payload, '{"name":"Existing"}');
    await store.save("scores", "legacy", "{}", 1, 0);
    await store.setDeletePending("skins", "legacy", true, 1);
    assert.deepEqual(await store.list("skins"), []);
    assert.equal((await store.list("skins", true)).length, 1);
    await store.close();
    const reopened = createRecordStore(factory, "upgrade");
    assert.equal((await reopened.list("skins", true))[0].deletePending, true);
    await reopened.close();
});

test("multi-record import commits together and rolls back every write on a stale revision", async () => {
    const store = createRecordStore(new IDBFactory());
    const changes = [
        {collection:"beatmapSets",id:"set",payload:'{"name":"Test"}',schemaVersion:1,expectedRevision:0},
        {collection:"beatmaps",id:"map",payload:'{"setId":"set"}',schemaVersion:1,expectedRevision:0}
    ];
    const created = await store.saveBatch(changes);
    assert.deepEqual(created.map(record => record.revision), [1,1]);
    await assert.rejects(store.saveBatch([
        {...changes[0],payload:'{"name":"Must roll back"}',expectedRevision:1},
        {...changes[1],expectedRevision:0}
    ]), /conflict/);
    assert.deepEqual(await store.get("beatmapSets", "set"), created[0]);
    assert.deepEqual(await store.get("beatmaps", "map"), created[1]);
    await assert.rejects(store.saveBatch([changes[0], changes[0]]), /same record/);
    await assert.rejects(store.saveBatch([{...changes[0],id:"new"},{...changes[1],payload:"broken JSON"}]));
    assert.equal(await store.get("beatmapSets", "new"), null);
    assert.deepEqual(await store.saveBatch([]), []);
    await store.close();
});

test("committed metadata survives closing and reopening, and collections stay isolated", async () => {
    const factory = new IDBFactory();
    const first = createRecordStore(factory);
    const written = await first.save("skins", "one", '{"name":"Default"}', 1, 0);
    await first.close();
    const second = createRecordStore(factory);
    assert.deepEqual(await second.get("skins", "one"), written);
    assert.equal(await second.get("scores", "one"), null);
    await second.close();
});

test("two connections cannot silently overwrite each other's changes", async () => {
    const factory = new IDBFactory();
    const first = createRecordStore(factory);
    const second = createRecordStore(factory);
    await first.save("skins", "one", "{}", 1, 0);
    const results = await Promise.allSettled([
        first.save("skins", "one", '{"name":"A"}', 1, 1),
        second.save("skins", "one", '{"name":"B"}', 1, 1)
    ]);
    assert.equal(results.filter(r => r.status === "fulfilled").length, 1);
    assert.match(results.find(r => r.status === "rejected").reason.message, /conflict/);
    assert.equal((await first.get("skins", "one")).revision, 2);
    await first.close();
    await second.close();
});

test("deletion and restoration preserve payload and are idempotent", async () => {
    const store = createRecordStore(new IDBFactory());
    await store.save("skins", "one", '{"name":"Keep"}', 1, 0);
    const deleted = await store.setDeletePending("skins", "one", true, 1);
    assert.equal(deleted.revision, 2);
    assert.equal(deleted.deletePending, true);
    assert.deepEqual(await store.setDeletePending("skins", "one", true, 2), deleted);
    const restored = await store.setDeletePending("skins", "one", false, 2);
    assert.equal(restored.payload, '{"name":"Keep"}');
    assert.equal(restored.deletePending, false);
    assert.equal(await store.setDeletePending("skins", "missing", false, 0), null);
    await store.close();
});

test("schema downgrade and stale writes fail without changing committed data", async () => {
    const store = createRecordStore(new IDBFactory());
    const saved = await store.save("scores", "one", "{}", 2, 0);
    await assert.rejects(store.save("scores", "one", "{}", 1, 1), /older schema/);
    await assert.rejects(store.save("scores", "one", "{}", 2, 0), /conflict/);
    assert.deepEqual(await store.get("scores", "one"), saved);
    assert.throws(() => store.save("scores", "bad", "not JSON", 1, 0));
    assert.equal(await store.get("scores", "bad"), null);
    await store.close();
});

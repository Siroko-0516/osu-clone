// Metadata storage only. Audio, textures and other large files use a separate file store.
export function createRecordStore(indexedDB, databaseName = "osu-web-records") {
    let connection;

    function open() {
        if (connection) return connection;
        connection = new Promise((resolve, reject) => {
            let abandoned = false;
            const request = indexedDB.open(databaseName, 2);
            request.onupgradeneeded = () => {
                const records = request.result.objectStoreNames.contains("records")
                    ? request.transaction.objectStore("records")
                    : request.result.createObjectStore("records", { keyPath: ["collection", "id"] });
                if (!records.indexNames.contains("collection")) records.createIndex("collection", "collection");
            };
            request.onerror = () => { connection = undefined; reject(request.error); };
            request.onblocked = () => {
                abandoned = true;
                connection = undefined;
                reject(new Error("Storage upgrade blocked: close other game tabs."));
            };
            request.onsuccess = () => {
                const database = request.result;
                if (abandoned) { database.close(); return; }
                database.onversionchange = () => { database.close(); connection = undefined; };
                resolve(database);
            };
        });
        return connection;
    }

    function validateKey(collection, id) {
        if (typeof collection !== "string" || !collection.trim() || typeof id !== "string" || !id.trim())
            throw new TypeError("A collection and record ID are required.");
    }

    async function transact(collection, id, mode, mutate) {
        validateKey(collection, id);
        const database = await open();
        return new Promise((resolve, reject) => {
            const transaction = database.transaction("records", mode);
            const store = transaction.objectStore("records");
            let result = null;
            let failure;
            // A successful request is not sufficient: report success only after commit.
            transaction.oncomplete = () => resolve(result);
            transaction.onabort = () => reject(failure || transaction.error || new Error("Storage transaction aborted."));
            const request = store.get([collection, id]);
            request.onsuccess = () => {
                try {
                    result = mutate(request.result || null);
                    if (mode === "readwrite" && result) store.put(result);
                } catch (error) {
                    failure = error;
                    transaction.abort();
                }
            };
        });
    }

    function checkRevision(record, expectedRevision) {
        if (!Number.isSafeInteger(expectedRevision) || expectedRevision < 0)
            throw new TypeError("Expected revision must be a nonnegative safe integer.");
        if ((record?.revision || 0) !== expectedRevision)
            throw new Error("Storage conflict: reload the record before saving.");
        if (expectedRevision === Number.MAX_SAFE_INTEGER)
            throw new RangeError("Record revision limit reached.");
    }

    function validateSave(change) {
        validateKey(change.collection, change.id);
        if (typeof change.payload !== "string") throw new TypeError("Payload must be JSON text.");
        JSON.parse(change.payload);
        if (!Number.isSafeInteger(change.schemaVersion) || change.schemaVersion < 1)
            throw new TypeError("A positive payload schema version is required.");
        if (!Number.isSafeInteger(change.expectedRevision) || change.expectedRevision < 0)
            throw new TypeError("Expected revision must be a nonnegative safe integer.");
    }

    function applySave(record, change) {
        checkRevision(record, change.expectedRevision);
        if (record && change.schemaVersion < record.schemaVersion)
            throw new Error("Cannot overwrite a record with an older schema version.");
        return { collection:change.collection, id:change.id, payload:change.payload,
            schemaVersion:change.schemaVersion, revision:change.expectedRevision + 1,
            deletePending:record?.deletePending || false };
    }

    return {
        get(collection, id) {
            return transact(collection, id, "readonly", record => record);
        },
        async list(collection, includeDeletePending = false) {
            validateKey(collection, "list");
            if (typeof includeDeletePending !== "boolean") throw new TypeError("Deletion filter must be a boolean.");
            const database = await open();
            return new Promise((resolve, reject) => {
                const transaction = database.transaction("records", "readonly");
                const request = transaction.objectStore("records").index("collection").getAll(collection);
                transaction.oncomplete = () => resolve(request.result.filter(record => includeDeletePending || !record.deletePending));
                transaction.onabort = () => reject(transaction.error || new Error("Storage read aborted."));
            });
        },
        async saveBatch(changes) {
            if (!Array.isArray(changes)) throw new TypeError("A list of record changes is required.");
            // Snapshot caller-owned objects before the first await.
            changes = changes.map(change => ({...change}));
            const keys = new Set();
            for (const change of changes) {
                validateSave(change);
                const key = JSON.stringify([change.collection, change.id]);
                if (keys.has(key)) throw new Error("A batch cannot contain the same record twice.");
                keys.add(key);
            }
            if (!changes.length) return [];
            const database = await open();
            return new Promise((resolve, reject) => {
                const transaction = database.transaction("records", "readwrite");
                const records = transaction.objectStore("records");
                const results = new Array(changes.length);
                let failure;
                transaction.oncomplete = () => resolve(results);
                transaction.onabort = () => reject(failure || transaction.error || new Error("Storage batch aborted."));
                changes.forEach((change, index) => {
                    const request = records.get([change.collection, change.id]);
                    request.onsuccess = () => {
                        try {
                            results[index] = applySave(request.result || null, change);
                            records.put(results[index]);
                        } catch (error) { failure = error; transaction.abort(); }
                    };
                });
            });
        },
        save(collection, id, payload, schemaVersion, expectedRevision) {
            const change = {collection, id, payload, schemaVersion, expectedRevision};
            validateSave(change);
            return transact(collection, id, "readwrite", record => applySave(record, change));
        },
        setDeletePending(collection, id, value, expectedRevision) {
            if (typeof value !== "boolean") throw new TypeError("Deletion state must be a boolean.");
            return transact(collection, id, "readwrite", record => {
                checkRevision(record, expectedRevision);
                if (!record || record.deletePending === value) return record;
                return { ...record, deletePending: value, revision: record.revision + 1 };
            });
        },
        async close() {
            const pending = connection;
            connection = undefined;
            if (pending) (await pending).close();
        }
    };
}

let browserStore;
function getBrowserStore() {
    return browserStore ??= createRecordStore(window.indexedDB);
}
export const get = (collection, id) => getBrowserStore().get(collection, id);
export const list = (...args) => getBrowserStore().list(...args);
export const saveBatch = changes => getBrowserStore().saveBatch(changes);
export const save = (...args) => getBrowserStore().save(...args);
export const setDeletePending = (...args) => getBrowserStore().setDeletePending(...args);

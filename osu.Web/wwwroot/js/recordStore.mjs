// Metadata storage only. Audio, textures and other large files use a separate file store.
export function createRecordStore(indexedDB, databaseName = "osu-web-records") {
    let connection;

    function open() {
        if (connection) return connection;
        connection = new Promise((resolve, reject) => {
            let abandoned = false;
            const request = indexedDB.open(databaseName, 1);
            request.onupgradeneeded = () => {
                request.result.createObjectStore("records", { keyPath: ["collection", "id"] });
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

    return {
        get(collection, id) {
            return transact(collection, id, "readonly", record => record);
        },
        save(collection, id, payload, schemaVersion, expectedRevision) {
            if (typeof payload !== "string") throw new TypeError("Payload must be JSON text.");
            JSON.parse(payload);
            if (!Number.isSafeInteger(schemaVersion) || schemaVersion < 1)
                throw new TypeError("A positive payload schema version is required.");
            return transact(collection, id, "readwrite", record => {
                checkRevision(record, expectedRevision);
                if (record && schemaVersion < record.schemaVersion)
                    throw new Error("Cannot overwrite a record with an older schema version.");
                return { collection, id, payload, schemaVersion, revision: expectedRevision + 1,
                    deletePending: record?.deletePending || false };
            });
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
export const save = (...args) => getBrowserStore().save(...args);
export const setDeletePending = (...args) => getBrowserStore().setDeletePending(...args);

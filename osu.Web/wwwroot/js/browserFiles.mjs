export function createBrowserFileStore(indexedDB, databaseName = 'osu-web-files') {
    let connection;
    const open = () => connection ??= new Promise((resolve, reject) => {
        const request = indexedDB.open(databaseName, 1);
        request.onupgradeneeded = () => request.result.createObjectStore('files', { keyPath: 'path' });
        request.onerror = () => { connection = undefined; reject(request.error); };
        request.onsuccess = () => {
            request.result.onversionchange = () => { request.result.close(); connection = undefined; };
            resolve(request.result);
        };
    });
    const validPath = path => typeof path === 'string' && path.length > 0 && !path.startsWith('/') && !path.split(/[\\/]/).includes('..');
    return {
        async count() {
            const database = await open();
            return new Promise((resolve, reject) => {
                const request = database.transaction('files').objectStore('files').count();
                request.onsuccess = () => resolve(request.result);
                request.onerror = () => reject(request.error);
            });
        },
        async keys(prefix) {
            if (!validPath(prefix.replace(/\/$/, ''))) throw new TypeError('A safe relative path prefix is required.');
            const database = await open();
            return new Promise((resolve, reject) => {
                const range = IDBKeyRange.bound(prefix, prefix + '\uffff');
                const request = database.transaction('files').objectStore('files').getAllKeys(range);
                request.onsuccess = () => resolve(request.result);
                request.onerror = () => reject(request.error);
            });
        },
        async get(path) {
            if (!validPath(path)) throw new TypeError('A safe relative file path is required.');
            const database = await open();
            return new Promise((resolve, reject) => {
                const request = database.transaction('files').objectStore('files').get(path.replaceAll('\\\\', '/'));
                request.onsuccess = () => resolve(request.result
                    ? { path: request.result.path, data: new Uint8Array(request.result.data) }
                    : null);
                request.onerror = () => reject(request.error);
            });
        },
        async put(entries) {
            const snapshot = entries.map(entry => {
                if (!validPath(entry.path)) throw new TypeError('A safe relative file path is required.');
                return { path: entry.path.replaceAll('\\', '/'), data: new Uint8Array(entry.data) };
            });
            if (!snapshot.length) return 0;
            const database = await open();
            return new Promise((resolve, reject) => {
                const transaction = database.transaction('files', 'readwrite');
                const store = transaction.objectStore('files');
                for (const entry of snapshot) store.put(entry);
                transaction.oncomplete = () => resolve(snapshot.length);
                transaction.onabort = transaction.onerror = () => reject(transaction.error || new Error('File write transaction failed.'));
            });
        },
        async replaceAll(entries) {
            const snapshot = entries.map(entry => {
                if (!validPath(entry.path)) throw new TypeError('A safe relative file path is required.');
                return { path: entry.path.replaceAll('\\', '/'), data: new Uint8Array(entry.data) };
            });
            const database = await open();
            return new Promise((resolve, reject) => {
                const transaction = database.transaction('files', 'readwrite');
                const store = transaction.objectStore('files');
                store.clear();
                for (const entry of snapshot) store.put(entry);
                transaction.oncomplete = () => resolve(snapshot.length);
                transaction.onabort = transaction.onerror = () => reject(transaction.error || new Error('File snapshot transaction failed.'));
            });
        },
        async close() { if (connection) (await connection).close(); connection = undefined; }
    };
}

let defaultStore;
const store = () => defaultStore ??= createBrowserFileStore(globalThis.indexedDB);
export const count = () => store().count();
export const keys = prefix => store().keys(prefix);
export const get = path => store().get(path);
export const put = entries => store().put(entries);
export const replaceAll = entries => store().replaceAll(entries);
export const close = () => store().close();

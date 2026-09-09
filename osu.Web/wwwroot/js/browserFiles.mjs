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
        async list() {
            const database = await open();
            return new Promise((resolve, reject) => {
                const request = database.transaction('files').objectStore('files').getAll();
                request.onsuccess = () => resolve(request.result.map(entry => ({ path: entry.path, data: new Uint8Array(entry.data) })));
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
export const list = () => store().list();
export const put = entries => store().put(entries);
export const replaceAll = entries => store().replaceAll(entries);
export const close = () => store().close();

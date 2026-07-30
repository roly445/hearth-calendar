export function isOnline() {
    return navigator.onLine;
}

const databaseName = 'hearth-calendar-offline';
const databaseVersion = 1;
const storeName = 'offline-items';

export async function readItem(key) {
    const database = await openDatabase();

    return await new Promise((resolve, reject) => {
        const transaction = database.transaction(storeName, 'readonly');
        const store = transaction.objectStore(storeName);
        const request = store.get(key);
        request.onsuccess = () => resolve(request.result ?? null);
        request.onerror = () => reject(request.error);
    });
}

export async function writeItem(key, value) {
    const database = await openDatabase();

    await new Promise((resolve, reject) => {
        const transaction = database.transaction(storeName, 'readwrite');
        const store = transaction.objectStore(storeName);
        const request = store.put(value, key);
        request.onsuccess = () => resolve();
        request.onerror = () => reject(request.error);
    });
}

export async function removeItem(key) {
    const database = await openDatabase();

    await new Promise((resolve, reject) => {
        const transaction = database.transaction(storeName, 'readwrite');
        const store = transaction.objectStore(storeName);
        const request = store.delete(key);
        request.onsuccess = () => resolve();
        request.onerror = () => reject(request.error);
    });
}

let onlineHandler;

export function startOnlineListener(dotNetReference) {
    if (onlineHandler) {
        window.removeEventListener('online', onlineHandler);
    }

    onlineHandler = () => dotNetReference.invokeMethodAsync('NotifyBrowserOnlineAsync');
    window.addEventListener('online', onlineHandler);
}

export function stopOnlineListener() {
    if (!onlineHandler) {
        return;
    }

    window.removeEventListener('online', onlineHandler);
    onlineHandler = undefined;
}

function openDatabase() {
    return new Promise((resolve, reject) => {
        const request = indexedDB.open(databaseName, databaseVersion);

        request.onupgradeneeded = () => {
            const database = request.result;
            if (!database.objectStoreNames.contains(storeName)) {
                database.createObjectStore(storeName);
            }
        };

        request.onsuccess = () => resolve(request.result);
        request.onerror = () => reject(request.error);
    });
}

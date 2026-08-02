interface DotNetReference {
    invokeMethodAsync(methodName: 'NotifyBrowserOnlineAsync'): Promise<void>;
}

export function isOnline(): boolean {
    return navigator.onLine;
}

const databaseName = 'hearth-calendar-offline';
const databaseVersion = 1;
const storeName = 'offline-items';

export async function readItem(key: string): Promise<string | null> {
    const database = await openDatabase();

    return await new Promise<string | null>((resolve, reject) => {
        const transaction = database.transaction(storeName, 'readonly');
        const store = transaction.objectStore(storeName);
        const request = store.get(key);
        request.onsuccess = () => resolve(typeof request.result === 'string' ? request.result : null);
        request.onerror = () => reject(request.error);
    });
}

export async function writeItem(key: string, value: string): Promise<void> {
    const database = await openDatabase();

    await new Promise<void>((resolve, reject) => {
        const transaction = database.transaction(storeName, 'readwrite');
        const store = transaction.objectStore(storeName);
        const request = store.put(value, key);
        request.onsuccess = () => resolve();
        request.onerror = () => reject(request.error);
    });
}

export async function removeItem(key: string): Promise<void> {
    const database = await openDatabase();

    await new Promise<void>((resolve, reject) => {
        const transaction = database.transaction(storeName, 'readwrite');
        const store = transaction.objectStore(storeName);
        const request = store.delete(key);
        request.onsuccess = () => resolve();
        request.onerror = () => reject(request.error);
    });
}

let onlineHandler: (() => void) | undefined;

export function startOnlineListener(dotNetReference: DotNetReference): void {
    if (onlineHandler) {
        window.removeEventListener('online', onlineHandler);
    }

    onlineHandler = () => dotNetReference.invokeMethodAsync('NotifyBrowserOnlineAsync');
    window.addEventListener('online', onlineHandler);
}

export function stopOnlineListener(): void {
    if (!onlineHandler) {
        return;
    }

    window.removeEventListener('online', onlineHandler);
    onlineHandler = undefined;
}

function openDatabase(): Promise<IDBDatabase> {
    return new Promise<IDBDatabase>((resolve, reject) => {
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

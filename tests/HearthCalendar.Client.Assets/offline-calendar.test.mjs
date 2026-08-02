import assert from 'node:assert/strict';
import { test } from 'node:test';

const moduleUrl = new URL('../../src/HearthCalendar.Client/wwwroot/offline-calendar.js', import.meta.url);

test('offline calendar reports browser online state', async () => {
    installBrowserFakes();
    const calendar = await importFreshModule();

    setNavigatorOnline(true);
    assert.equal(calendar.isOnline(), true);

    setNavigatorOnline(false);
    assert.equal(calendar.isOnline(), false);
});

test('offline calendar stores reads and removes indexed db items', async () => {
    installBrowserFakes();
    const calendar = await importFreshModule();

    assert.equal(await calendar.readItem('missing'), null);

    await calendar.writeItem('event-intent', '{"title":"Dentist"}');
    assert.equal(await calendar.readItem('event-intent'), '{"title":"Dentist"}');

    await calendar.removeItem('event-intent');
    assert.equal(await calendar.readItem('event-intent'), null);
});

test('offline calendar online listener replaces and removes handlers', async () => {
    const browser = installBrowserFakes();
    const calendar = await importFreshModule();
    let firstCalls = 0;
    let secondCalls = 0;

    calendar.startOnlineListener({
        invokeMethodAsync(methodName) {
            assert.equal(methodName, 'NotifyBrowserOnlineAsync');
            firstCalls += 1;
            return Promise.resolve();
        }
    });
    calendar.startOnlineListener({
        invokeMethodAsync(methodName) {
            assert.equal(methodName, 'NotifyBrowserOnlineAsync');
            secondCalls += 1;
            return Promise.resolve();
        }
    });

    browser.dispatch('online');
    assert.equal(firstCalls, 0);
    assert.equal(secondCalls, 1);

    calendar.stopOnlineListener();
    browser.dispatch('online');
    assert.equal(secondCalls, 1);
});

async function importFreshModule() {
    return await import(`${moduleUrl.href}?test=${crypto.randomUUID()}`);
}

function installBrowserFakes() {
    const listeners = new Map();
    const indexedDb = new FakeIndexedDb();

    Object.defineProperty(globalThis, 'navigator', {
        configurable: true,
        value: { onLine: true }
    });
    Object.defineProperty(globalThis, 'indexedDB', {
        configurable: true,
        value: indexedDb
    });
    Object.defineProperty(globalThis, 'window', {
        configurable: true,
        value: {
            addEventListener(type, listener) {
                listeners.set(type, listener);
            },
            removeEventListener(type, listener) {
                if (listeners.get(type) === listener) {
                    listeners.delete(type);
                }
            }
        }
    });

    return {
        dispatch(type) {
            listeners.get(type)?.();
        }
    };
}

function setNavigatorOnline(isOnline) {
    Object.defineProperty(globalThis, 'navigator', {
        configurable: true,
        value: { onLine: isOnline }
    });
}

class FakeIndexedDb {
    #database = new FakeDatabase();

    open() {
        const request = createRequest(this.#database);
        queueMicrotask(() => {
            request.onupgradeneeded?.();
            request.onsuccess?.();
        });

        return request;
    }
}

class FakeDatabase {
    #stores = new Map();

    objectStoreNames = {
        contains: name => this.#stores.has(name)
    };

    createObjectStore(name) {
        this.#stores.set(name, new Map());
    }

    transaction(name) {
        if (!this.#stores.has(name)) {
            this.createObjectStore(name);
        }

        return new FakeTransaction(this.#stores.get(name));
    }
}

class FakeTransaction {
    constructor(store) {
        this.store = store;
    }

    objectStore() {
        return new FakeObjectStore(this.store);
    }
}

class FakeObjectStore {
    constructor(store) {
        this.store = store;
    }

    get(key) {
        return completeRequest(this.store.get(key));
    }

    put(value, key) {
        this.store.set(key, value);
        return completeRequest(undefined);
    }

    delete(key) {
        this.store.delete(key);
        return completeRequest(undefined);
    }
}

function completeRequest(result) {
    const request = createRequest(result);
    queueMicrotask(() => request.onsuccess?.());
    return request;
}

function createRequest(result) {
    return {
        error: null,
        result,
        onerror: undefined,
        onsuccess: undefined,
        onupgradeneeded: undefined
    };
}

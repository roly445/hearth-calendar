interface DevelopmentServiceWorkerExtendableEvent {
    waitUntil(promise: Promise<unknown>): void;
}

interface DevelopmentServiceWorkerGlobalScope {
    clients: {
        claim(): Promise<void>;
    };
    skipWaiting(): Promise<void>;
    addEventListener(type: 'install', listener: (event: DevelopmentServiceWorkerExtendableEvent) => void): void;
    addEventListener(type: 'activate', listener: (event: DevelopmentServiceWorkerExtendableEvent) => void): void;
    addEventListener(type: 'fetch', listener: () => void): void;
}

const developmentServiceWorker = globalThis as unknown as DevelopmentServiceWorkerGlobalScope;

// In development, always fetch from the network and do not enable offline support.
// This is because caching would make development more difficult because changes would
// not be reflected on the first load after each change.
developmentServiceWorker.addEventListener('install', event => {
    event.waitUntil(developmentServiceWorker.skipWaiting());
});
developmentServiceWorker.addEventListener('activate', event => {
    event.waitUntil(developmentServiceWorker.clients.claim());
});
developmentServiceWorker.addEventListener('fetch', () => { });

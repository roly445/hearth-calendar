interface DevelopmentServiceWorkerGlobalScope {
    addEventListener(type: 'fetch', listener: () => void): void;
}

const developmentServiceWorker = globalThis as unknown as DevelopmentServiceWorkerGlobalScope;

// In development, always fetch from the network and do not enable offline support.
// This is because caching would make development more difficult because changes would
// not be reflected on the first load after each change.
developmentServiceWorker.addEventListener('fetch', () => { });

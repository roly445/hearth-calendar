interface PublishedServiceWorkerAsset {
    hash: string;
    url: string;
}

interface PublishedServiceWorkerAssetsManifest {
    assets: PublishedServiceWorkerAsset[];
    version: string;
}

interface PublishedServiceWorkerExtendableEvent {
    waitUntil(promise: Promise<unknown>): void;
}

interface PublishedServiceWorkerFetchEvent {
    request: Request;
    respondWith(response: Promise<Response>): void;
}

interface PublishedServiceWorkerGlobalScope {
    assetsManifest: PublishedServiceWorkerAssetsManifest;
    origin: string;
    clients: {
        claim(): Promise<void>;
    };
    importScripts(path: string): void;
    skipWaiting(): Promise<void>;
    addEventListener(type: 'install', listener: (event: PublishedServiceWorkerExtendableEvent) => void): void;
    addEventListener(type: 'activate', listener: (event: PublishedServiceWorkerExtendableEvent) => void): void;
    addEventListener(type: 'fetch', listener: (event: PublishedServiceWorkerFetchEvent) => void): void;
}

const publishedServiceWorker = globalThis as unknown as PublishedServiceWorkerGlobalScope;

// Caution! Be sure you understand the caveats before publishing an application with
// offline support. See https://aka.ms/blazor-offline-considerations
publishedServiceWorker.importScripts('./service-worker-assets.js');
publishedServiceWorker.addEventListener('install', event => event.waitUntil(onPublishedServiceWorkerInstall()));
publishedServiceWorker.addEventListener('activate', event => event.waitUntil(onPublishedServiceWorkerActivate()));
publishedServiceWorker.addEventListener('fetch', event => event.respondWith(onPublishedServiceWorkerFetch(event)));

const publishedServiceWorkerCacheNamePrefix = 'offline-cache-';
const publishedServiceWorkerCacheName = `${publishedServiceWorkerCacheNamePrefix}${publishedServiceWorker.assetsManifest.version}`;
const publishedServiceWorkerOfflineAssetsInclude = [ /\.dll$/, /\.pdb$/, /\.wasm/, /\.html/, /\.js$/, /\.css$/, /\.woff$/, /\.png$/, /\.jpe?g$/, /\.gif$/, /\.ico$/, /\.blat$/, /\.dat$/, /\.webmanifest$/ ];
const publishedServiceWorkerOfflineAssetsExclude = [ /^service-worker\.js$/ ];
const publishedServiceWorkerOfflineJsonAssetsInclude = [ /^_framework\/blazor\.boot\.json$/ ];
const publishedServiceWorkerNeverCacheRequestPath = [ /^\/commands\//, /^\/queries\//, /^\/hubs\//, /^\/auth\//, /^\/feeds\//, /^\/api\// ];

// Replace with your base path if you are hosting on a subfolder. Ensure there is a trailing '/'.
const publishedServiceWorkerBase = '/';
const publishedServiceWorkerBaseUrl = new URL(publishedServiceWorkerBase, publishedServiceWorker.origin);
const publishedServiceWorkerManifestUrlList = publishedServiceWorker.assetsManifest.assets
    .map(asset => new URL(asset.url, publishedServiceWorkerBaseUrl).href);

async function onPublishedServiceWorkerInstall(): Promise<void> {
    console.info('Service worker: Install');

    const assetsRequests = publishedServiceWorker.assetsManifest.assets
        .filter(asset => isPublishedServiceWorkerCacheableOfflineAsset(asset.url))
        .filter(asset => !publishedServiceWorkerOfflineAssetsExclude.some(pattern => pattern.test(asset.url)))
        .map(asset => new Request(asset.url, { integrity: asset.hash, cache: 'no-cache' }));
    await caches.open(publishedServiceWorkerCacheName).then(cache => cache.addAll(assetsRequests));
    await publishedServiceWorker.skipWaiting();
}

async function onPublishedServiceWorkerActivate(): Promise<void> {
    console.info('Service worker: Activate');

    const cacheKeys = await caches.keys();
    await Promise.all(cacheKeys
        .filter(key => key.startsWith(publishedServiceWorkerCacheNamePrefix) && key !== publishedServiceWorkerCacheName)
        .map(key => caches.delete(key)));
    await publishedServiceWorker.clients.claim();
}

async function onPublishedServiceWorkerFetch(event: PublishedServiceWorkerFetchEvent): Promise<Response> {
    const requestUrl = new URL(event.request.url);
    if (requestUrl.origin !== publishedServiceWorker.origin
        || publishedServiceWorkerNeverCacheRequestPath.some(pattern => pattern.test(requestUrl.pathname))) {
        return fetch(event.request);
    }

    if (isPublishedServiceWorkerNavigationRequest(event.request)) {
        try {
            return await fetch(event.request);
        } catch (error) {
            const cache = await caches.open(publishedServiceWorkerCacheName);
            const cachedResponse = await cache.match('index.html');

            return cachedResponse ?? Promise.reject(error);
        }
    }

    let cachedResponse: Response | undefined;
    if (event.request.method === 'GET') {
        const cache = await caches.open(publishedServiceWorkerCacheName);
        cachedResponse = await cache.match(event.request);
    }

    return cachedResponse ?? fetch(event.request);
}

function isPublishedServiceWorkerNavigationRequest(request: Request): boolean {
    return request.method === 'GET'
        && request.mode === 'navigate'
        && !publishedServiceWorkerManifestUrlList.some(url => url === request.url);
}

function isPublishedServiceWorkerCacheableOfflineAsset(assetUrl: string): boolean {
    return publishedServiceWorkerOfflineAssetsInclude.some(pattern => pattern.test(assetUrl))
        || publishedServiceWorkerOfflineJsonAssetsInclude.some(pattern => pattern.test(assetUrl));
}

import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { test } from 'node:test';

const workerRoot = new URL('../../src/HearthCalendar.Client/wwwroot/', import.meta.url);

test('development service worker registers a network-first fetch handler', async () => {
    const source = await readGeneratedWorker('service-worker.js');

    assert.match(source, /addEventListener\('fetch'/);
    assert.match(source, /skipWaiting/);
    assert.match(source, /clients\.claim/);
    assert.doesNotMatch(source, /caches\.open/);
    assert.doesNotMatch(source, /importScripts/);
});

test('published service worker keeps dynamic endpoints out of offline cache', async () => {
    const source = await readGeneratedWorker('service-worker.published.js');

    assert.match(source, /importScripts\('\.\/service-worker-assets\.js'\)/);
    assert.match(source, /JsonAssetsInclude/);
    assert.match(source, /skipWaiting/);
    assert.match(source, /clients\.claim/);
    assert.match(source, /isPublishedServiceWorkerNavigationRequest/);
    assert.match(source, /return await fetch\(event\.request\)/);
    assert.doesNotMatch(source, /\/\\\.json\$\/$/);

    for (const endpoint of ['commands', 'queries', 'hubs', 'auth', 'feeds', 'api']) {
        assert.match(source, new RegExp(`\\^\\\\/${endpoint}\\\\/`));
    }
});

async function readGeneratedWorker(fileName) {
    return await readFile(new URL(fileName, workerRoot), 'utf8');
}

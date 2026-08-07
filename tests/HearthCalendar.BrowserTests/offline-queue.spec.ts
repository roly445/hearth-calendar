import { expect, test } from "@playwright/test";
import { signInAsBrowserAdmin } from "./browser-admin";

test.describe("offline event queue", () => {
  test("queues an event while offline and syncs it after reconnecting", async ({ page }) => {
    const title = `Family offline sync ${Date.now()}`;
    const createPanel = page.locator(".create-panel");

    await signInAsBrowserAdmin(page);

    await expect(page.getByRole("heading", { name: "Hearth Calendar" })).toBeVisible({ timeout: 30_000 });
    await expect(page.getByText("Online")).toBeVisible();
    await expect(page.getByRole("heading", { name: "Family planning" })).toBeVisible();
    await waitForCachedUpcomingEvent(page, "Family planning");

    await page.context().setOffline(true);
    await page.getByRole("button", { name: "Refresh" }).click();

    await expect(page.getByText("Offline")).toBeVisible();
    await expect(page.getByText("Stale", { exact: true })).toBeVisible();
    await expect(page.getByRole("heading", { name: "Family planning" })).toBeVisible();

    await createPanel.getByLabel("Event").fill(title);
    await createPanel.getByLabel("Date").fill("2026-08-21");
    await createPanel.getByLabel("Start").fill("18:15");
    await createPanel.getByLabel("End").fill("19:15");
    await createPanel.getByRole("button", { name: "Create" }).click();

    const queuedItem = page.locator("article.queued-item").filter({
      has: page.getByText(title)
    });
    await expect(page.getByText("Event queued offline. It will sync for review when the app reconnects.")).toBeVisible();
    await expect(queuedItem).toBeVisible();
    await expect(queuedItem.getByText("Pending")).toBeVisible();

    await page.context().setOffline(false);
    await page.evaluate(() => window.dispatchEvent(new Event("online")));

    await expect(page.getByText("Queued event synced and is waiting for server review.")).toBeVisible({ timeout: 30_000 });
    await expect(queuedItem).toBeHidden();
    await expect(page.getByText("Online")).toBeVisible();

    const syncedEvent = page.locator(".events-panel article.event-row").filter({
      has: page.getByRole("heading", { name: title })
    });
    await expect(syncedEvent).toHaveCount(1);
    await expect(syncedEvent.first()).toBeVisible();
    await expect(syncedEvent.getByText("18:15 to 19:15 - Family - adult-a, adult-b, child")).toBeVisible();
  });
});

async function waitForCachedUpcomingEvent(page: import("@playwright/test").Page, title: string) {
  await page.waitForFunction(
    async expectedTitle => {
      const database = await new Promise<IDBDatabase>((resolve, reject) => {
        const request = indexedDB.open("hearth-calendar-offline", 1);
        request.onsuccess = () => resolve(request.result);
        request.onerror = () => reject(request.error);
      });

      const snapshot = await new Promise<string | undefined>((resolve, reject) => {
        const transaction = database.transaction("offline-items", "readonly");
        const store = transaction.objectStore("offline-items");
        const request = store.get("hearth-calendar:offline:upcoming-snapshot");
        request.onsuccess = () => resolve(request.result);
        request.onerror = () => reject(request.error);
      });

      return typeof snapshot === "string" && snapshot.includes(expectedTitle);
    },
    title,
    { timeout: 30_000 }
  );
}

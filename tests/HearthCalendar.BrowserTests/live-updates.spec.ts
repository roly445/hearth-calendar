import { expect, test } from "@playwright/test";
import { signInAsBrowserAdmin } from "./browser-admin";

test.describe("live calendar updates", () => {
  test("refreshes another open workspace after review queue and calendar event changes", async ({ browser }) => {
    const sourceContext = await browser.newContext();
    const targetContext = await browser.newContext();
    const source = await sourceContext.newPage();
    const target = await targetContext.newPage();
    const suffix = Date.now().toString();
    const stagedTitle = `dentist live update ${suffix}`;
    const approvedTitle = `Family live update ${suffix}`;
    const createPanel = source.locator(".create-panel");

    try {
      await signInAsBrowserAdmin(target);
      await signInAsBrowserAdmin(source);

      await expect(target.getByRole("heading", { name: "Hearth Calendar" })).toBeVisible({ timeout: 30_000 });
      await expect(source.getByRole("heading", { name: "Hearth Calendar" })).toBeVisible({ timeout: 30_000 });
      await expect(target.getByRole("heading", { name: "Adult A dentist" })).toBeVisible();

      await createPanel.getByLabel("Event").fill(stagedTitle);
      await createPanel.getByRole("button", { name: "Create" }).click();

      await expect(source.getByRole("heading", { name: stagedTitle })).toBeVisible({ timeout: 30_000 });
      await expect(target.getByRole("heading", { name: stagedTitle })).toBeVisible({ timeout: 30_000 });

      await createPanel.getByLabel("Event").fill(approvedTitle);
      await createPanel.getByLabel("Date").fill("2026-08-20");
      await createPanel.getByLabel("Start").fill("19:00");
      await createPanel.getByLabel("End").fill("20:00");
      await createPanel.getByRole("button", { name: "Create" }).click();

      await expect(source.getByText("Review item Approved.")).toBeVisible({ timeout: 30_000 });
      const liveEvent = target.locator("article.event-row").filter({
        has: target.getByRole("heading", { name: approvedTitle })
      });
      await expect(liveEvent).toBeVisible({ timeout: 30_000 });
      await expect(liveEvent.getByText("19:00 to 20:00 - Family - adult-a, adult-b, child")).toBeVisible();
    } finally {
      await sourceContext.close();
      await targetContext.close();
    }
  });
});

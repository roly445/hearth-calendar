import { expect, test } from "@playwright/test";

test.describe("submit event intent", () => {
  test("creates a generic approved event through the workspace form", async ({ page }) => {
    await page.goto("/");

    await expect(page.getByRole("heading", { name: "Hearth Calendar" })).toBeVisible({ timeout: 30_000 });

    await page.getByLabel("Event").fill("Family board game");
    await page.getByLabel("Date").fill("2026-08-20");
    await page.getByLabel("Start").fill("17:30");
    await page.getByLabel("End").fill("18:30");
    await page.getByRole("button", { name: "Create" }).click();

    await expect(page.getByText("Review item Approved.")).toBeVisible();
    await expect(page.getByRole("heading", { name: "Upcoming" })).toBeVisible();

    const submittedEvent = page.locator("article.event-row").filter({
      has: page.getByRole("heading", { name: "Family board game" })
    });
    await expect(submittedEvent).toBeVisible();
    await expect(submittedEvent.getByText("17:30 to 18:30 - Family - adult-a, adult-b, child")).toBeVisible();
    await expect(submittedEvent.getByText("Family", { exact: true })).toBeVisible();
  });
});

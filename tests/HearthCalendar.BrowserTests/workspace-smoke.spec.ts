import { expect, test } from "@playwright/test";

test.describe("calendar workspace smoke", () => {
  test("loads the authenticated workspace with generic test data", async ({ page }) => {
    await page.goto("/");

    await expect(page.getByRole("heading", { name: "Hearth Calendar" })).toBeVisible({ timeout: 30_000 });
    await expect(page.getByText("Online")).toBeVisible();
    await expect(page.getByRole("button", { name: "Refresh" })).toBeVisible();
    await expect(page.getByRole("button", { name: "Sign out" })).toBeVisible();

    await expect(page.getByRole("heading", { name: "Create Event" })).toBeVisible();
    await expect(page.getByLabel("Event")).toBeVisible();
    await expect(page.getByRole("button", { name: "Create" })).toBeVisible();

    await expect(page.getByRole("heading", { name: "Review Queue" })).toBeVisible();
    await expect(page.getByRole("heading", { name: "Adult A dentist" })).toBeVisible();
    await expect(page.getByText("PastEvent: Past non-reference events need confirmation.")).toBeVisible();

    await expect(page.getByRole("heading", { name: "Upcoming" })).toBeVisible();
    await expect(page.getByRole("heading", { name: "Family planning" })).toBeVisible();
    await expect(page.getByText("Family", { exact: true })).toBeVisible();
  });
});

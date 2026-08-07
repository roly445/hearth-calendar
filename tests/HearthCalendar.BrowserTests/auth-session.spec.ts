import { expect, Page, test } from "@playwright/test";

const baseURL = process.env.HEARTH_CALENDAR_BROWSER_BASE_URL ?? "http://127.0.0.1:5179";
const browserTestAuthCookie = {
  name: "hearth-browser-test-auth",
  value: "anonymous",
  url: baseURL
};

async function forceAnonymousMode(page: Page) {
  await page.context().addCookies([browserTestAuthCookie]);
}

async function signInAsBrowserAdmin(page: Page) {
  await forceAnonymousMode(page);
  await page.goto("/");

  await expect(page).toHaveURL(/\/login$/);
  await page.getByLabel("Username").fill("browser-test-admin");
  await page.getByLabel("Password").fill("browser-test-password");
  await page.getByRole("button", { name: "Sign in" }).click();

  await expect(page.getByRole("heading", { name: "Hearth Calendar" })).toBeVisible({ timeout: 30_000 });
}

test.describe("authenticated admin session", () => {
  test("unauthenticated users are sent to the sign-in flow", async ({ page }) => {
    await forceAnonymousMode(page);

    await page.goto("/");

    await expect(page).toHaveURL(/\/login$/);
    await expect(page.getByRole("heading", { name: "Sign In" })).toBeVisible();
    await expect(page.getByRole("heading", { name: "Review Queue" })).toBeHidden();
    await expect(page.getByRole("heading", { name: "Upcoming" })).toBeHidden();
  });

  test("authenticated admins can load protected workspace data", async ({ page }) => {
    await signInAsBrowserAdmin(page);

    await expect(page.getByRole("heading", { name: "Review Queue" })).toBeVisible();
    await expect(page.getByRole("heading", { name: "Adult A dentist" })).toBeVisible();
    await expect(page.getByRole("heading", { name: "Upcoming" })).toBeVisible();
    await expect(page.getByRole("heading", { name: "Family planning" })).toBeVisible();
  });

  test("session loss stops protected actions and asks the admin to sign in again", async ({ page }) => {
    await signInAsBrowserAdmin(page);

    const reviewItem = page.locator("article.review-item").filter({
      has: page.getByRole("heading", { name: "Adult A dentist" })
    });
    await expect(reviewItem).toBeVisible();

    await page.context().clearCookies();
    await forceAnonymousMode(page);
    await reviewItem.getByRole("button", { name: "Approve" }).click();

    await expect(page).toHaveURL(/\/login$/);
    await expect(page.getByRole("heading", { name: "Sign In" })).toBeVisible();
  });

  test("signing out clears protected workspace access", async ({ page }) => {
    await signInAsBrowserAdmin(page);

    await page.getByRole("button", { name: "Sign out" }).click();

    await expect(page).toHaveURL(/\/login$/);
    await expect(page.getByRole("heading", { name: "Sign In" })).toBeVisible();
    await expect(page.getByRole("heading", { name: "Review Queue" })).toBeHidden();

    const session = await page.request.get("/api/admin/session");
    expect(session.status()).toBe(401);
  });
});

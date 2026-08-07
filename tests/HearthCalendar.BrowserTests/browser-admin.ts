import { expect, Page } from "@playwright/test";

export async function signInAsBrowserAdmin(page: Page) {
  await page.goto("/");

  await expect(page).toHaveURL(/\/login$/);
  await page.getByLabel("Username").fill("browser-test-admin");
  await page.getByLabel("Password").fill("browser-test-password");
  await page.getByRole("button", { name: "Sign in" }).click();

  await expect(page.getByRole("heading", { name: "Hearth Calendar" })).toBeVisible({ timeout: 30_000 });
}

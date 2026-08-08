import { expect, test } from "@playwright/test";
import { signInAsBrowserAdmin } from "./browser-admin";

test.describe("household metadata admin", () => {
  test("lists generic default members and relationships", async ({ page }) => {
    await signInAsBrowserAdmin(page);
    await page.getByRole("link", { name: "Household" }).click();

    await expect(page.getByRole("heading", { name: "Household Metadata" })).toBeVisible({ timeout: 30_000 });
    await expect(page.getByRole("heading", { name: "Adult A", exact: true })).toBeVisible();
    await expect(page.getByRole("heading", { name: "Adult B", exact: true })).toBeVisible();
    await expect(page.getByRole("heading", { name: "Child", exact: true })).toBeVisible();
    await expect(page.getByText("ParentOrGuardianOf - Active").first()).toBeVisible();
  });

  test("creates generic member and relationship metadata", async ({ page }) => {
    await signInAsBrowserAdmin(page);
    await page.getByRole("link", { name: "Household" }).click();

    await page.getByLabel("Member id").fill("child-a");
    await page.getByLabel("Display label").fill("Child A");
    await page.getByLabel("Member kind").selectOption("Child");
    await page.getByRole("button", { name: "Add member" }).click();

    await expect(page.getByText("Household member created.")).toBeVisible();
    await expect(page.getByRole("heading", { name: "Child A" })).toBeVisible();
    await expect(page.getByText("child-a - Child - Active")).toBeVisible();

    await page.getByLabel("From member").selectOption("adult-a");
    await page.getByLabel("To member").selectOption("child-a");
    await page.getByLabel("Relationship kind").selectOption("ParentOrGuardianOf");
    await page.getByRole("button", { name: "Add relationship" }).click();

    await expect(page.getByText("Household relationship created.")).toBeVisible();
    await expect(page.getByRole("heading", { name: "Adult A to Child A" })).toBeVisible();
    await expect(page.getByText("ParentOrGuardianOf - Active").last()).toBeVisible();
  });
});

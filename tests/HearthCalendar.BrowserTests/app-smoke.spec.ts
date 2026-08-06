import { expect, test } from "@playwright/test";

test.describe("hosted app smoke", () => {
  test("serves the anonymous health endpoint", async ({ request }) => {
    const response = await request.get("/health");
    const headers = response.headers();

    await expect(response).toBeOK();
    expect(headers["content-security-policy"]).toContain("default-src 'self'");
    expect(headers["x-content-type-options"]).toBe("nosniff");
    expect(headers["x-frame-options"]).toBe("DENY");
    expect(await response.json()).toEqual({ status: "Healthy" });
  });

  test("reaches the running app from a real browser", async ({ page }) => {
    await page.goto("/health");

    await expect(page.locator("body")).toContainText("Healthy");
  });
});

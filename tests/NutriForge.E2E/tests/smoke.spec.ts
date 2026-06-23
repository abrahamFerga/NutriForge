import { test, expect } from "@playwright/test";

/**
 * Smoke E2E for the NutriForge SPA. Requires the stack running (the SPA talks to the API, which
 * uses dev-auth so the browser is "signed in" automatically). Run with the stack up:
 *   E2E_BASE_URL=http://localhost:5173 npm test
 */
test.describe("NutriForge shell", () => {
  test("loads the dashboard shell and navigates the pillars", async ({ page }) => {
    await page.goto("/");

    // The app chrome renders.
    await expect(page.getByText("NutriForge").first()).toBeVisible();

    // Route-per-pillar navigation is present and reachable.
    await page.getByRole("link", { name: /diary/i }).click();
    await expect(page).toHaveURL(/\/diary/);

    await page.getByRole("link", { name: /profile/i }).click();
    await expect(page).toHaveURL(/\/profile/);
  });

  test("opens the always-present NutritionAssistant panel", async ({ page }) => {
    await page.goto("/");
    // The assistant drawer is reachable from every route.
    await expect(page.getByRole("dialog", { name: /nutrition assistant/i })).toBeAttached();
  });
});

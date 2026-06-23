import { defineConfig, devices } from "@playwright/test";

/**
 * E2E config. Point at a running SPA via E2E_BASE_URL (the Vite dev server from
 * `dotnet run --project src/NutriForge.AppHost`, or a deployed/preview URL). Defaults to the
 * local Vite port. In CI the specs are listed/compiled always; the live run is gated on
 * E2E_BASE_URL being set (see .github/workflows/ci.yml).
 */
export default defineConfig({
  testDir: "./tests",
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  reporter: process.env.CI ? "github" : "list",
  use: {
    baseURL: process.env.E2E_BASE_URL ?? "http://localhost:5173",
    trace: "on-first-retry",
    screenshot: "only-on-failure",
  },
  projects: [{ name: "chromium", use: { ...devices["Desktop Chrome"] } }],
});

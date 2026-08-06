import { defineConfig, devices } from "@playwright/test";

const port = Number.parseInt(process.env.HEARTH_CALENDAR_BROWSER_PORT ?? "5179", 10);
const baseURL = process.env.HEARTH_CALENDAR_BROWSER_BASE_URL ?? `http://127.0.0.1:${port}`;

export default defineConfig({
  testDir: "./tests/HearthCalendar.BrowserTests",
  fullyParallel: false,
  forbidOnly: Boolean(process.env.CI),
  retries: process.env.CI ? 1 : 0,
  reporter: process.env.CI ? [["github"], ["list"]] : "list",
  use: {
    baseURL,
    trace: "on-first-retry"
  },
  webServer: {
    command: `dotnet run --project src/HearthCalendar.Server/HearthCalendar.Server.csproj --no-launch-profile --urls ${baseURL}`,
    url: `${baseURL}/health`,
    reuseExistingServer: !process.env.CI,
    timeout: 120_000,
    env: {
      ASPNETCORE_ENVIRONMENT: "Test",
      Database__ConnectionString:
        "Host=127.0.0.1;Port=1;Database=hearth_calendar_browser;Username=browser;Password=browser",
      Database__SchemaName: "hearth_calendar_browser"
    }
  },
  projects: [
    {
      name: "chromium",
      use: { ...devices["Desktop Chrome"] }
    }
  ]
});

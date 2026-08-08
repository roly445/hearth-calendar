import { defineConfig, devices } from "@playwright/test";

const port = Number.parseInt(process.env.HEARTH_CALENDAR_BROWSER_PORT ?? "5179", 10);
const baseURL = process.env.HEARTH_CALENDAR_BROWSER_BASE_URL ?? `http://127.0.0.1:${port}`;

export default defineConfig({
  testDir: "./tests/HearthCalendar.BrowserTests",
  fullyParallel: false,
  forbidOnly: Boolean(process.env.CI),
  retries: process.env.CI ? 1 : 0,
  reporter: process.env.CI ? [["github"], ["list"], ["html", { open: "never" }]] : "list",
  use: {
    baseURL,
    screenshot: "on",
    trace: "on-first-retry",
    video: "on"
  },
  webServer: {
    command: `dotnet run --configuration Release --project src/HearthCalendar.Server/HearthCalendar.Server.csproj --no-restore --no-launch-profile --urls ${baseURL}`,
    url: `${baseURL}/health`,
    reuseExistingServer: false,
    timeout: 120_000,
    env: {
      ASPNETCORE_ENVIRONMENT: "Test",
      BrowserTests__UseSeedData: "true",
      Auth__AdminUsers__0__Username: "browser-test-admin",
      Auth__AdminUsers__0__DisplayName: "Browser Test Admin",
      Auth__AdminUsers__0__Password: "browser-test-password",
      Auth__AdminUsers__0__Scopes__0: "admin:web",
      Auth__PasswordHasher__WorkFactor: "4",
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

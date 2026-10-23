import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { defineConfig, devices } from '@playwright/test'

const API_URL = 'http://127.0.0.1:5199'
const CLIENT_URL = 'http://127.0.0.1:5174'

// Workers re-evaluate this file. Setting the path once on the environment they inherit keeps
// every process on the same fresh database, which the API seeds on startup.
process.env.ATM_E2E_DB ??= join(tmpdir(), `atm-e2e-${Date.now()}.db`)

export default defineConfig({
  testDir: './e2e',
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  reporter: process.env.CI ? 'github' : 'list',
  use: {
    baseURL: CLIENT_URL,
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  projects: [
    {
      name: 'full-stack',
      testDir: './e2e/full-stack',
      // Every test shares one API database, so they run one at a time.
      fullyParallel: false,
      workers: 1,
      use: { ...devices['Desktop Chrome'] },
    },
    {
      name: 'mocked',
      testDir: './e2e/mocked',
      fullyParallel: true,
      use: { ...devices['Desktop Chrome'] },
    },
  ],
  webServer: [
    {
      name: 'api',
      command: `dotnet run --project ../api --no-launch-profile --urls ${API_URL}`,
      url: `${API_URL}/api/atm/accounts`,
      env: {
        ASPNETCORE_ENVIRONMENT: 'Development',
        ConnectionStrings__Atm: `Data Source=${process.env.ATM_E2E_DB};Default Timeout=5`,
      },
      // The first run builds the API.
      timeout: 180_000,
      reuseExistingServer: false,
    },
    {
      name: 'client',
      // Vite's default "localhost" can bind only ::1 on Windows, which the 127.0.0.1 probe misses.
      command: 'npx vite --host 127.0.0.1 --port 5174 --strictPort',
      url: CLIENT_URL,
      env: { API_URL },
      timeout: 60_000,
      reuseExistingServer: false,
    },
  ],
})

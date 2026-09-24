import { defineConfig, devices } from '@playwright/test'

// Runs against a live stack: `docker compose up -d` (web :5173, api, db, workos emulator).
// Override with E2E_BASE_URL, e.g. the Vite dev server.
// The global setup drops and reseeds the stack's database first (E2E_RESET=0 skips it). Specs share
// personas (Maria appears in four of them), so they run one at a time, in file order.
export default defineConfig({
  testDir: 'e2e',
  globalSetup: './e2e/global-setup.ts',
  fullyParallel: false,
  workers: 1,
  retries: 0,
  reporter: [['list']],
  use: {
    baseURL: process.env.E2E_BASE_URL ?? 'http://localhost:5173',
    trace: 'retain-on-failure',
  },
  projects: [
    { name: 'desktop', use: { ...devices['Desktop Chrome'], viewport: { width: 1440, height: 1000 } } },
    { name: 'phone', use: { ...devices['Pixel 7'] }, grep: /@phone/ },
  ],
})

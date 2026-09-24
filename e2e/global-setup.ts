import { execFileSync } from 'node:child_process'
import { fileURLToPath } from 'node:url'

// The demo paths change fixed personas for good (Sam's first sign-in, Maria's first registration and
// retreat application, a code approved once, duplicates merged once), so every run starts from the
// seed: stop the api, drop its database, and start it again so it migrates and seeds.
// Skipped when E2E_BASE_URL points at another server, or with E2E_RESET=0.
const root = fileURLToPath(new URL('..', import.meta.url))
const baseURL = 'http://localhost:5173'

function compose(...args: string[]) {
  execFileSync('docker', ['compose', ...args], { cwd: root, stdio: 'inherit' })
}

async function waitForApi(deadline: number) {
  for (;;) {
    try {
      if ((await fetch(`${baseURL}/api/programs`)).ok) return
    } catch {
      // The api is still starting.
    }
    if (Date.now() > deadline) throw new Error('The api did not come back after the database reset.')
    await new Promise((resolve) => setTimeout(resolve, 1000))
  }
}

export default async function globalSetup() {
  if (process.env.E2E_RESET === '0') return
  if (process.env.E2E_BASE_URL && process.env.E2E_BASE_URL !== baseURL) return

  compose('stop', 'api')
  compose(
    'exec',
    '-T',
    'db',
    'sh',
    '-c',
    `/opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -b -Q "IF DB_ID('CampRegistration') IS NOT NULL BEGIN ALTER DATABASE CampRegistration SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE CampRegistration; END"`,
  )
  compose('start', 'api')
  await waitForApi(Date.now() + 120_000)
}

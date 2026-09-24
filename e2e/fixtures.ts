import { expect, type Page, test as base } from '@playwright/test'

/** Seeded personas in infra/workos/workos-emulate.config.yaml. */
export const personas = {
  maria: 'maria.johnson@example.com', // returning family: Avery (G6) and Mia (G4)
  david: 'david.johnson@example.com', // Maria's co-owner, same Johnson household
  sam: 'sam.rivera@example.com', // new family, no household until first sign-in
  pastorDave: 'pastor.dave@example.com', // church group leader
  diane: 'diane.carter@winshape.example', // staff, CET
  marcus: 'marcus.lee@winshape.example', // staff, finance
  grace: 'grace.patel@winshape.example', // host coordinator
  alex: 'alex.morgan@winshape.example', // staff, admin (setup)
} as const

// The app's own origin: the emulator's sign-in page is on another port (4100, or a slice's private one).
const appOrigin = new URL(process.env.E2E_BASE_URL ?? 'http://localhost:5173').origin

/** Signs in through the WorkOS emulator's hosted page, then waits to land back in the app. */
export async function signInAs(page: Page, persona: keyof typeof personas, returnTo = '/') {
  await page.goto(`/api/auth/login?returnTo=${encodeURIComponent(returnTo)}`)
  await page.getByRole('textbox', { name: 'Email' }).fill(personas[persona])
  await page.getByRole('button', { name: 'Continue' }).click()
  await page.waitForURL((url) => url.origin === appOrigin && !url.pathname.startsWith('/api/'))
}

export const test = base
export { expect }

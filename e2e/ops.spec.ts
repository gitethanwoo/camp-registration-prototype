import type { Page } from '@playwright/test'
import { expect, signInAs, test } from './fixtures'

// Slice 7 demo: Diane (Customer Experience) runs Overnight Camp Session 3 from the Operations pages:
// readiness and reminders (O1), a group move with Undo (O2), a cabin placement and rooming review (O3),
// and the arrival desk with a blocked override, a clean check-in and a verified pickup (O5). Alex (admin)
// sees the same boards. Each test picks its own rows from the API by status, so it can run on a
// database earlier runs already changed.

interface Scope {
  programs: { slug: string; sessions: { id: number; name: string }[] }[]
}
interface CheckInRow {
  registrationId: number
  name: string
  status: 'Ready' | 'Blocked' | 'Checked in' | 'Checked out'
  pickupAdults: { id: number; name: string }[]
}

/** Points the admin scope at Overnight Camp Session 3 and returns its id. */
async function useSession3(page: Page) {
  const scope = (await (await page.request.get('/api/admin/scope')).json()) as Scope[]
  const session = scope
    .flatMap((m) => m.programs)
    .find((p) => p.slug === 'overnight-camp')
    ?.sessions.find((s) => s.name === 'Session 3')
  expect(session, 'Overnight Camp Session 3 is seeded').toBeTruthy()
  const id = session?.id ?? 0
  await page.evaluate((v) => localStorage.setItem('admin.sessionId', String(v)), id)
  return id
}

async function checkInRows(page: Page, sessionId: number) {
  const res = await page.request.get(`/api/admin/ops/sessions/${sessionId}/check-in`)
  return ((await res.json()) as { rows: CheckInRow[] }).rows
}

test.describe.configure({ mode: 'serial' })

test('Diane sees who is not ready for camp and sends reminders', async ({ page }) => {
  await signInAs(page, 'diane', '/admin')
  const sessionId = await useSession3(page)
  // A family reminded in the last 24 hours isn't emailed again, so pick a camper with a balance
  // due who hasn't been reminded yet (and whose name is unique, so the checkbox is unambiguous).
  const roster = (
    (await (await page.request.get(`/api/admin/ops/sessions/${sessionId}/readiness`)).json()) as {
      roster: { name: string; reasons: string[]; remindedAt: string | null }[]
    }
  ).roster
  const target = roster.find(
    (r) => r.reasons.includes('Balance') && !r.remindedAt && roster.filter((x) => x.name === r.name).length === 1,
  )
  expect(target, 'a camper with a balance due who has not been reminded is left').toBeTruthy()
  await page.goto('/admin/ops/readiness')

  await expect(page.getByRole('heading', { name: 'Session readiness' })).toBeVisible()
  await expect(page.getByText('/ 200').first()).toBeVisible()
  await expect(page.getByTestId('overlap-note')).toContainText('Reasons overlap')
  await expect(page.getByTestId('capacity-pools')).toContainText('Full')

  await page.getByRole('combobox', { name: 'Filter by status' }).click()
  await page.getByRole('option', { name: 'Balance due' }).click()
  await page.getByLabel('Search campers').fill(target?.name ?? '')
  await page.getByRole('checkbox', { name: `Select ${target?.name}` }).click()
  await page.getByRole('button', { name: /Send reminders \(1\)/ }).click()
  await expect(page.getByRole('alertdialog')).toContainText('Each family gets one email from HubSpot')
  await page.getByRole('button', { name: 'Send reminders' }).last().click()
  await expect(page.getByText(/Reminders queued for 1 family/)).toBeVisible()
  await expect(page.getByRole('cell', { name: /Reminded / }).first()).toBeVisible()
})

test('Diane moves a camper to another group and undoes it', async ({ page }) => {
  await signInAs(page, 'diane', '/admin')
  await useSession3(page)
  await page.goto('/admin/ops/groups')
  await expect(page.getByRole('heading', { name: 'Group assignments' })).toBeVisible()

  await page.getByLabel('Capacity pool').click()
  await page.getByRole('option', { name: /^Boys G3–5/ }).click()
  await expect(page.getByText(/Boys G3–5 · 46 campers/)).toBeVisible()

  // Boys G3–5 has 46 campers in five groups of 10, so the last group has room.
  const firstGroup = page.locator('[data-testid^="group-"]').first()
  const card = firstGroup.locator('[data-testid^="camper-"]').first()
  const cardId = await card.getAttribute('data-testid')
  const name = (await card.locator('p').first().innerText()).trim()
  await card.getByRole('button', { name: `Move ${name} to another group` }).click()
  await page.getByRole('menuitem', { name: /Group 5/ }).click()
  await expect(page.getByText(`Saved · ${name} is now in Group 5.`)).toBeVisible()
  await expect(
    page
      .locator('[data-testid^="group-"]')
      .nth(4)
      .getByTestId(cardId ?? ''),
  ).toBeVisible()

  await page.getByRole('button', { name: 'Undo' }).click()
  await expect(firstGroup.getByTestId(cardId ?? '')).toBeVisible()
})

test('Diane places an unassigned camper in a cabin and marks rooming reviewed', async ({ page }) => {
  await signInAs(page, 'diane', '/admin')
  await useSession3(page)
  await page.goto('/admin/ops/rooming')
  await expect(page.getByRole('heading', { name: 'Rooming board' })).toBeVisible()
  await expect(page.getByRole('heading', { name: 'Boys cabins (8)' })).toBeVisible()
  await expect(page.getByRole('heading', { name: 'Girls cabins (8)' })).toBeVisible()

  const row = page.getByTestId('unassigned').locator('li').first()
  const name = (await row.locator('p').first().innerText()).split('\n')[0].replace('Needs review', '').trim()
  await row.getByRole('button', { name: `Move ${name} to another cabin` }).click()
  const open = page.getByRole('menuitem', { name: /Cabin/ }).and(page.locator(':not([data-disabled])')).first()
  await open.click()
  await expect(page.getByText(/^Saved · moved to .*Cabin \d\.$/)).toBeVisible()

  const changed = page.getByTestId('roster-changed')
  if (await changed.isVisible()) {
    await changed.getByRole('button', { name: 'Mark reviewed' }).click()
    await expect(page.getByText(/Rooming marked reviewed/)).toBeVisible()
    await expect(changed).toBeHidden()
  }
  await expect(page.getByText(/Reviewed by Diane Carter/)).toBeVisible()
})

test('Diane checks in a blocked camper only with a reason, then checks a camper in and out', async ({ page }) => {
  await signInAs(page, 'diane', '/admin')
  const sessionId = await useSession3(page)
  const rows = await checkInRows(page, sessionId)
  const blocked = rows.find((r) => r.status === 'Blocked')
  const ready = rows.find((r) => r.status === 'Ready' && r.pickupAdults.length > 0)
  expect(blocked, 'a blocked camper is left').toBeTruthy()
  expect(ready, 'a ready camper is left').toBeTruthy()

  await page.goto('/admin/ops/check-in')
  const search = page.getByLabel('Search a name, or scan the confirmation code')
  const panel = page.getByTestId('check-in-panel')

  // Blocked: check-in stays disabled until staff give an override reason.
  await search.fill(blocked?.name ?? '')
  await page.getByRole('cell', { name: blocked?.name }).first().click()
  await expect(panel.getByText('Check-in blocked')).toBeVisible()
  await expect(panel.getByRole('button', { name: 'Confirm check-in' })).toBeDisabled()
  await panel.getByRole('button', { name: 'Check in anyway with a reason' }).click()
  await page.getByRole('button', { name: 'Check in with override' }).click()
  await expect(page.getByText('Say why this camper is being checked in with open items.')).toBeVisible()
  await page.getByLabel('Reason').fill('Parent showed the signed waiver on paper; office will scan it today.')
  await page.getByRole('button', { name: 'Check in with override' }).click()
  await expect(page.getByText(`${blocked?.name} is checked in.`)).toBeVisible()
  await expect(panel.getByText(/Checked in with open items\. Reason: Parent showed/)).toBeVisible()

  // Ready: one tap to check in, then pickup by an authorized adult after an ID check.
  await search.fill(ready?.name ?? '')
  await page.getByRole('cell', { name: ready?.name }).first().click()
  await panel.getByRole('button', { name: 'Confirm check-in' }).click()
  await expect(page.getByText(`${ready?.name} is checked in.`)).toBeVisible()

  await page.getByRole('tab', { name: 'Check-out' }).click()
  await page.getByRole('cell', { name: ready?.name }).first().click()
  await panel.getByRole('radio').first().click()
  await panel.getByRole('button', { name: 'Confirm check-out' }).click()
  await expect(panel.getByText('Confirm you checked their photo ID.')).toBeVisible()
  await panel.getByRole('checkbox').click()
  await panel.getByRole('button', { name: 'Confirm check-out' }).click()
  await expect(page.getByText(`${ready?.name} is checked out.`)).toBeVisible()
  await expect(panel.getByText(/Picked up by .*\((Parent|Grand(mother|father))\)/)).toBeVisible()
})

test('Alex (admin) opens every operations page from the sidebar', async ({ page }) => {
  await signInAs(page, 'alex', '/admin')
  await useSession3(page)
  await page.reload()
  for (const [link, heading] of [
    ['Session readiness', 'Session readiness'],
    ['Groups', 'Group assignments'],
    ['Rooming', 'Rooming board'],
    ['Check-in', 'Check-in and check-out'],
  ]) {
    await page.getByRole('link', { name: link, exact: true }).click()
    await expect(page.getByRole('heading', { name: heading, level: 1 })).toBeVisible()
  }
  await expect(
    page.getByRole('button', { name: 'Confirm check-in' }).or(page.getByText('Search or pick a camper')),
  ).toBeVisible()
})

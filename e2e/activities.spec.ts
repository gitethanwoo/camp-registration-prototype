import type { Browser, Page } from '@playwright/test'
import { expect, signInAs, test } from './fixtures'

// Activities slice demo on Overnight Camp Session 3: Pastor Dave registers a new daughter, ranks
// activities, sees Climbing fill while he chooses (Diane moves a camper into its last place), takes the
// suggested next choice and asks for a cabinmate by confirmation code. Alex edits the catalog (K8), and
// Diane opens the schedule builder (O4) and assigns waiting campers from their preferences.
// The spec changes seeded rows for good, so it runs once per fresh database, in order.
test.describe.configure({ mode: 'serial' })

interface Scope {
  programs: { slug: string; sessions: { id: number; name: string }[] }[]
}
interface Cell {
  slotId: number
  period: number
  assigned: number
  capacity: number
  campers: { registrationId: number; name: string; grade: number | null; doubleBooked: boolean }[]
}
interface Grid {
  block: { id: number; name: string }
  rows: { name: string; gradeMin: number; cells: Cell[] }[]
}

async function session3(page: Page) {
  const scope = (await (await page.request.get('/api/admin/scope')).json()) as Scope[]
  const id =
    scope
      .flatMap((m) => m.programs)
      .find((p) => p.slug === 'overnight-camp')
      ?.sessions.find((s) => s.name === 'Session 3')?.id ?? 0
  expect(id, 'Overnight Camp Session 3 is seeded').toBeGreaterThan(0)
  await page.evaluate((v) => localStorage.setItem('admin.sessionId', String(v)), id)
  return id
}

/** Diane fills Climbing's last Period 2 place in Juniors by moving a camper there, as staff would. */
async function fillClimbingPeriod2(browser: Browser) {
  const staff = await browser.newPage()
  await signInAs(staff, 'diane', '/admin')
  const id = await session3(staff)
  const grid = (await (await staff.request.get(`/api/admin/ops/sessions/${id}/activities`)).json()) as Grid
  expect(grid.block.name).toBe('Juniors')
  const climbing = grid.rows.find((r) => r.name === 'Climbing')?.cells.find((c) => c.period === 2)
  expect(climbing?.capacity).toBe(24)
  const open = (climbing?.capacity ?? 0) - (climbing?.assigned ?? 0)
  const movers = grid.rows
    .filter((r) => r.name !== 'Climbing')
    .flatMap((r) =>
      (r.cells.find((c) => c.period === 2) ?? { campers: [], slotId: 0 }).campers.map((c) => ({
        c,
        from: r.cells.find((x) => x.period === 2)?.slotId ?? 0,
      })),
    )
    .filter(({ c }) => (c.grade ?? 0) >= 4 && !c.doubleBooked)
    .slice(0, open)
  expect(movers.length).toBe(open)
  for (const { c, from } of movers) {
    const res = await staff.request.post(`/api/admin/ops/sessions/${id}/activities/move`, {
      data: { registrationId: c.registrationId, fromSlotId: from, toSlotId: climbing?.slotId },
    })
    expect(res.ok(), await res.text()).toBeTruthy()
  }
  // A friend for the cabinmate request: a camper already registered, found by confirmation code.
  const rows = (
    (await (await staff.request.get(`/api/admin/ops/sessions/${id}/check-in`)).json()) as {
      rows: { name: string; confirmationCode: string }[]
    }
  ).rows
  await staff.close()
  const friend = rows[0]
  expect(friend).toBeTruthy()
  return { name: friend?.name ?? '', code: friend?.confirmationCode ?? '' }
}

test('Pastor Dave registers a camper, sees an activity fill, takes the next choice and asks for a cabinmate', async ({
  page,
  browser,
}) => {
  await signInAs(page, 'pastorDave', '/family')
  const res = await page.request.post('/api/family/members', {
    data: { firstName: 'Hannah', lastName: 'Kim', dateOfBirth: '2018-10-01', gender: 'Female', isAdult: false },
  })
  // Re-running on the same database finds Hannah already in the family.
  expect(res.ok() || (await res.text()).includes('already in your family'), await res.text()).toBeTruthy()

  await page.goto('/programs/overnight-camp')
  await page.getByRole('button', { name: 'Register', exact: true }).click()
  await expect(page).toHaveURL(/\/register\/\d+/)
  await page.getByRole('checkbox', { name: 'Register Hannah' }).click()
  await page.getByRole('button', { name: 'Continue' }).click()

  // Questions: answer whatever the live form requires.
  const card = page.locator('[data-slot="card"]').filter({ hasText: 'About Hannah' })
  for (const box of await card.getByRole('combobox').all()) {
    await box.click()
    await page.getByRole('option').first().click()
  }
  const family = page.locator('[data-slot="card"]').filter({ hasText: 'About your family' })
  if (await family.count()) await family.getByRole('radio', { name: 'No' }).first().click()
  await page.getByRole('button', { name: 'Continue' }).click()

  // R4: one tab per camper would show here; Hannah is the only one. Rank Climbing then Canoeing in Period 2.
  await expect(page.getByRole('heading', { name: 'Choose activities' })).toBeVisible()
  const p1 = page.getByTestId('period-1')
  const p2 = page.getByTestId('period-2')
  const p3 = page.getByTestId('period-3')
  await p1.getByRole('button', { name: /^Archery, Period 1/ }).click()
  await p2.getByRole('button', { name: /^Climbing, Period 2/ }).click()
  await p2.getByRole('button', { name: /^Canoeing, Period 2/ }).click()
  await p3.getByRole('button', { name: /^Crafts, Period 3/ }).click()
  await expect(p2.getByRole('button', { name: /^Climbing, Period 2, choice 1: 1 slot left/ })).toBeVisible()

  // P3 from the step: details with "Select for Hannah".
  await p1.getByRole('button', { name: 'Swimming details' }).click()
  const sheet = page.getByRole('dialog')
  await expect(sheet.getByRole('heading', { name: 'Swimming' })).toBeVisible()
  await expect(sheet.getByText('Period 1 for Hannah')).toBeVisible()
  await sheet.getByRole('button', { name: 'Select for Hannah' }).click()
  await expect(p1.getByRole('button', { name: /^Swimming, Period 1, choice 2/ })).toBeVisible()

  // Climbing's last place goes to another camper while Dave is choosing.
  const friend = await fillClimbingPeriod2(browser)
  await page.getByRole('button', { name: 'Continue' }).click()
  await expect(p2.getByText('Just filled', { exact: true })).toBeVisible()
  await expect(p2.getByRole('status')).toContainText('Climbing just filled up')
  await expect(p2.getByRole('status')).toContainText("We'll place Hannah in Canoeing")
  await p2.getByRole('button', { name: 'Use Canoeing' }).click()
  await expect(p2.getByText('Just filled', { exact: true })).toHaveCount(0)
  await expect(p2.getByRole('button', { name: /^Canoeing, Period 2, choice 1/ })).toBeVisible()
  await page.getByRole('button', { name: 'Continue' }).click()

  // R5: never a room pick.
  await expect(page.getByRole('heading', { name: 'Cabinmate requests' })).toBeVisible()
  await expect(page.getByText("cabins aren't guaranteed")).toBeVisible()
  await page.getByLabel("Friend's full name").first().fill(friend.name)
  await page.getByLabel("Parent's email or friend code").first().fill(friend.code)
  await page.getByLabel("Friend's full name").nth(1).click()
  await expect(page.getByText(`We found ${friend.name} in this session`)).toBeVisible()
  await page.getByRole('button', { name: 'Continue' }).click()

  // Health (CampDoc for Overnight Camp), waivers, review, payment.
  await page.getByRole('button', { name: 'Continue' }).click()
  for (const box of await page.getByRole('checkbox', { name: /I agree/ }).all()) await box.click()
  const signer = page.getByLabel('Full name')
  if (!(await signer.inputValue())) await signer.fill('Dave Kim')
  await page.getByRole('button', { name: 'Continue' }).click()
  await expect(page.getByText('Activities: Archery · Canoeing · Crafts')).toBeVisible()
  await page.getByRole('radio', { name: 'Pay deposit' }).click()
  await page.getByRole('button', { name: 'Continue to payment' }).click()
  await page.getByRole('button', { name: '4242 4242 4242 4242' }).click()
  await page.getByRole('button', { name: /^Pay \$/ }).click()
  await expect(page.getByRole('heading', { name: 'You’re registered!' })).toBeVisible()

  // F1: Hannah is placed; the camper who joined the household earlier still needs to choose.
  await page.goto('/family')
  await expect(page.getByText('Hannah · Activities')).toBeVisible()
  await expect(page.getByText('Archery · Canoeing · Crafts')).toBeVisible()
  await expect(page.getByRole('link', { name: 'Choose activities' })).toBeVisible()
})

test('Alex edits an activity in the catalog and the change is audited', async ({ page }) => {
  await signInAs(page, 'alex', '/admin/setup/activities')
  await expect(page.getByRole('heading', { name: 'Activity catalog' })).toBeVisible()
  await page.getByRole('row').filter({ hasText: 'Canoeing' }).click()
  const sheet = page.getByRole('dialog')
  await expect(sheet.getByRole('heading', { name: 'Canoeing' })).toBeVisible()
  await sheet.getByLabel('What to bring').fill('Water shoes, a towel and a hat')
  await sheet.getByLabel('Space').fill('Lake dock B')
  await sheet.getByRole('button', { name: 'Save activity' }).click()
  await expect(page.getByText('Canoeing saved. The change is in the audit log.')).toBeVisible()
  await page.keyboard.press('Escape')
  await expect(page.getByRole('row').filter({ hasText: 'Canoeing' })).toContainText('Lake dock B')

  await page.goto('/admin/setup/audit')
  await expect(page.getByText('Edited Canoeing in the activity catalog.').first()).toBeVisible()
})

test('Diane opens the schedule builder, reviews conflicts and assigns from preferences', async ({ page }) => {
  await signInAs(page, 'diane', '/admin')
  await session3(page)
  await page.goto('/admin/ops/activities')
  await expect(page.getByRole('heading', { name: /Activity schedule/ })).toBeVisible()
  await expect(page.getByRole('tab', { name: /Juniors/ })).toBeVisible()
  await expect(page.getByText(/conflicts? to resolve/)).toBeVisible()
  const climbing = page.getByRole('button', { name: /^Climbing, Period 2: 24 of 24, Full/ })
  await expect(climbing).toBeVisible()

  await page.getByRole('button', { name: 'Assign from preferences' }).click()
  const confirm = page.getByRole('alertdialog')
  await expect(confirm).toContainText("Staff moves and campers who haven't chosen are left alone")
  await confirm.getByRole('button', { name: /^Assign \d+ places/ }).click()
  await expect(page.getByText(/^Placed \d+ periods? for \d+ Juniors campers?/)).toBeVisible()
  await expect(page.getByText('Everyone who chose has a place.')).toBeVisible()

  // Seniors: resolve the seeded double booking by keeping the camper in Climbing.
  await page.getByRole('tab', { name: /Seniors/ }).click()
  await expect(page.getByRole('button', { name: /^Swimming, Period 2: 25 of 24, Over capacity/ })).toBeVisible()
  await page.getByRole('button', { name: /^Climbing, Period 2:/ }).click()
  const sheet = page.getByRole('dialog')
  await sheet.getByRole('button', { name: 'Keep in Climbing' }).click()
  await expect(page.getByText(/stays in Climbing; the other booking is removed/)).toBeVisible()
  await page.keyboard.press('Escape')
  await expect(page.getByRole('button', { name: /^Swimming, Period 2: 24 of 24, Full/ })).toBeVisible()
})

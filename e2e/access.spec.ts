import { expect, signInAs, test, type Page } from './fixtures'

// Slice 10 demo path: Diane (CET) is refused a camper's health form, Alex (admin) syncs staff from WorkOS
// (the former staff member shows as Revoked), gives Diane health-data access and opens Day Camp's health
// details to Customer Experience, and Diane can then read the form. Every view lands in the audit log.
// The spec changes seeded rows for good, so it runs once per fresh database, in order.
test.describe.configure({ mode: 'serial' })

let registrationId = 0

/** A Day Camp · Atlanta registration with a health form on file, found through the admin API. */
async function dayCampRegistration(page: Page) {
  if (registrationId) return registrationId
  const programs = (await (await page.request.get('/api/programs')).json()) as {
    slug: string
    sessions: { id: number }[]
  }[]
  const sessions = programs.find((p) => p.slug === 'day-camp-atlanta')?.sessions ?? []
  for (const s of sessions) {
    const list = (await (await page.request.get(`/api/admin/registrations?sessionId=${s.id}&pageSize=100`)).json()) as {
      rows: { id: number; health: string; status: string }[]
    }
    const row = list.rows.find((r) => r.health === 'Complete' && r.status !== 'Cancelled')
    if (row) return (registrationId = row.id)
  }
  throw new Error('No Day Camp registration with a completed health form in the seed')
}

test('Diane is refused a Day Camp health form without health-data access', async ({ page }) => {
  await signInAs(page, 'diane', '/admin')
  const id = await dayCampRegistration(page)
  await page.goto(`/admin/registrations/${id}`)
  // Allergies and dietary needs are health details: not on the page, only behind the enforced dialog.
  await expect(page.getByRole('button', { name: 'View health form' })).toBeVisible()
  await expect(page.getByText('Allergies', { exact: true })).toHaveCount(0)
  await page.getByRole('button', { name: 'View health form' }).click()

  const dialog = page.getByRole('dialog')
  await expect(dialog.getByTestId('health-refused')).toContainText('completion status only')
  await expect(dialog).not.toContainText('Physician')
  await dialog.getByRole('button', { name: 'Close' }).first().click()

  // Admin pages stay closed to her: the role guard shows the console's No access page.
  await page.goto('/admin/setup/users')
  await expect(page).toHaveURL(/\/admin\/no-access/)
  await expect(page.getByRole('heading', { name: "You don't have access to this page" })).toBeVisible()
  await expect(page.getByText('This page needs the Administrator role.')).toBeVisible()
  await expect(page.getByRole('heading', { name: 'Staff access' })).toHaveCount(0)
})

test('Alex syncs staff from WorkOS and the former staff member is revoked', async ({ page }) => {
  await signInAs(page, 'alex', '/admin/setup/users')
  await expect(page.getByRole('heading', { name: 'Staff access' })).toBeVisible()
  await expect(page.getByTestId('last-sync')).toContainText('in WorkOS')

  await page.getByRole('tab', { name: /Revoked/ }).click()
  const morgan = page.getByRole('row').filter({ hasText: 'Morgan Ellis' })
  await expect(morgan).toContainText('Revoked')
  await morgan.click()
  const sheet = page.getByRole('dialog')
  await expect(sheet).toContainText('no longer in the WinShape Staff organization')
  await expect(sheet.getByText('access revoked').first()).toBeVisible()
  await page.keyboard.press('Escape')
})

test('Alex gives Diane health-data access and opens Day Camp to Customer Experience', async ({ page }) => {
  await signInAs(page, 'alex', '/admin/setup/users')
  await page.getByPlaceholder('Search staff').fill('Diane')
  const row = page.getByRole('row').filter({ hasText: 'Diane Carter' })
  await expect(row).toContainText('Completion status only')
  await row.click()

  const sheet = page.getByRole('dialog')
  await expect(sheet.getByText('Set in WorkOS')).toBeVisible()
  await sheet.getByLabel('Health data').click()
  await page.getByRole('option', { name: 'Health details' }).click()
  await expect(sheet.getByText('Unsaved changes')).toBeVisible()
  await sheet.getByRole('button', { name: 'Save access' }).click()
  await expect(page.getByText("Diane Carter's access saved.")).toBeVisible()
  await expect(
    sheet.getByText('Diane Carter can now view health details where a program allows their role.'),
  ).toBeVisible()
  await page.keyboard.press('Escape')
  await expect(row).toContainText('Health details')

  await page.goto('/admin/setup/health')
  await expect(page.getByRole('heading', { name: 'Health collection settings' })).toBeVisible()
  await page.getByRole('row').filter({ hasText: 'Day Camp · Atlanta' }).click()
  const editor = page.locator('#health-form')
  await expect(editor.getByRole('listitem').filter({ hasText: 'Diane Carter' })).toBeHidden()
  await editor.getByRole('checkbox', { name: 'Customer Experience' }).click()
  await page.getByRole('button', { name: 'Save settings' }).click()
  await expect(page.getByText('Health settings for Day Camp · Atlanta saved.')).toBeVisible()
  await expect(page.getByRole('row').filter({ hasText: 'Day Camp · Atlanta' })).toContainText(
    'Administrator, Customer Experience',
  )
  await expect(editor.getByText('Can view details now')).toBeVisible()
  await expect(editor.getByRole('listitem').filter({ hasText: 'Diane Carter' })).toBeVisible()
})

test('CampDoc outside Overnight Camp needs a deliberate confirm', async ({ page }) => {
  await signInAs(page, 'alex', '/admin/setup/health')
  await page.getByRole('row').filter({ hasText: 'Family Camp' }).first().click()
  await page.getByRole('radio', { name: /CampDoc/ }).click()
  await expect(page.getByText('CampDoc is used by Overnight Camp only.')).toBeVisible()
  const save = page.getByRole('button', { name: 'Save settings' })
  await expect(save).toBeDisabled()
  await page.getByRole('checkbox', { name: /anyway/ }).click()
  await expect(save).toBeEnabled()
  await page.getByRole('button', { name: 'Discard' }).click()
  await expect(page.getByText('CampDoc is used by Overnight Camp only.')).toBeHidden()
})

test('Diane reads the health form and the view is in the audit trail', async ({ page }) => {
  await signInAs(page, 'diane', '/admin')
  const id = await dayCampRegistration(page)
  await page.goto(`/admin/registrations/${id}`)
  await page.getByRole('button', { name: 'View health form' }).click()

  const dialog = page.getByRole('dialog')
  await expect(dialog.getByText('Physician', { exact: true })).toBeVisible()
  await expect(dialog.getByText(/^Dr\. /)).toBeVisible()
  await expect(dialog.getByText('Your view was recorded in the audit log.')).toBeVisible()
  await dialog.getByRole('button', { name: 'Close' }).first().click()

  // The registration's activity (loaded fresh) shows both the refusal and the view.
  await page.reload()
  await page.getByRole('tab', { name: 'Activity' }).click()
  await expect(page.getByText('health.viewed').first()).toBeVisible()
  await expect(page.getByText('health.view_denied').first()).toBeVisible()
})

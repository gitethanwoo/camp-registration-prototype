import { expect, signInAs, test } from './fixtures'

// Polish demo path on fresh seed data: the app reads as March 2, 2028; David lands in Maria's household;
// Family Camp's waiver is signed on Maria's checklist; a CET user gets a No access page naming the
// role a setup page needs; and an admin can't publish a program until it has a waiver.

test('David signs in to the Johnson household he shares with Maria', async ({ page }) => {
  await signInAs(page, 'david', '/family')
  await expect(page).toHaveURL(/\/family$/)
  await expect(page.getByRole('button', { name: /David Johnson/ })).toBeVisible()
  await expect(page.getByText('Maria').first()).toBeVisible()
  await expect(page.getByText('Avery').first()).toBeVisible()
})

test('Maria sees Family Camp with its waiver signed', async ({ page }) => {
  await signInAs(page, 'maria', '/family/registrations/WS-FC2J08')
  await expect(page.getByRole('heading', { name: /Family Camp/ }).first()).toBeVisible()
  const checklist = page.locator('#checklist')
  for (const kid of ['Avery', 'Mia']) {
    const row = checklist.locator('li').filter({ hasText: `${kid} · Family Camp Release and Waiver of Liability` })
    await expect(row).toContainText('Complete')
  }
  await expect(checklist.getByRole('button', { name: /Sign/ })).toHaveCount(0)
})

test('the app runs on the demo clock', async ({ page }) => {
  await signInAs(page, 'alex', '/admin/finance/reports')
  // The default range ends on the demo's "today", not the real date.
  await expect(page.getByText(/to Mar 2, 2028/)).toBeVisible()
})

test('a CET user who opens a setup page is told it needs the Administrator role', async ({ page }) => {
  await signInAs(page, 'diane', '/admin/setup/programs')
  await expect(page).toHaveURL(/\/no-access\?need=role/)
  await expect(page.getByText("You don't have access to this page")).toBeVisible()
  await expect(page.getByText(/needs the Administrator role/)).toBeVisible()
  // It renders inside the staff console, not the public site's header.
  await expect(page).toHaveURL(/\/admin\/no-access/)
  await expect(page.getByRole('link', { name: 'Session overview' })).toBeVisible()
  await expect(page.getByRole('link', { name: 'My family' })).toHaveCount(0)
  await page.getByRole('link', { name: 'Go to the staff console' }).click()
  await expect(page).toHaveURL(/\/admin$/)
})

test('an admin adds the standard release before a program can be published', async ({ page }) => {
  await signInAs(page, 'alex', '/admin/setup/programs')
  await expect(page.getByText('Overnight Camp').first()).toBeVisible()
  await page.getByRole('button', { name: 'New program' }).click()
  const name = `Spring Retreat ${Date.now().toString(36)}`
  await page.getByLabel('Program name').fill(name)
  await page.getByLabel('Location').fill('Mount Berry, GA')
  await page.getByRole('button', { name: 'Create draft' }).click()

  const sheet = page.getByRole('dialog')
  await expect(sheet.getByRole('heading', { name })).toBeVisible()
  await expect(sheet.getByText("a program can't be published without one")).toBeVisible()
  await sheet.getByRole('button', { name: 'Add the standard release' }).click()
  await expect(sheet.getByText(`${name} Release and Waiver of Liability v1`)).toBeVisible()
  await expect(sheet.getByRole('button', { name: 'Add the standard release' })).toHaveCount(0)
})

test('the No access page fits a phone @phone', async ({ page }) => {
  await signInAs(page, 'diane', '/admin/setup/programs')
  await expect(page.getByText(/needs the Administrator role/)).toBeVisible()
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth)
  expect(overflow).toBe(false)
})

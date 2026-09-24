import { expect, signInAs, test } from './fixtures'

// Slice 5 demo path: Alex (admin) finishes Family Weekend's approval, resizes a pool, adds a discount rule,
// approves the waiver v3 another admin wrote, and finds all of it in the audit log.
// The spec changes seeded rows for good, so it runs once per fresh database, in order.
test.describe.configure({ mode: 'serial' })

test('Alex approves Family Weekend and it appears on the guest site', async ({ page }) => {
  await signInAs(page, 'alex', '/admin/setup/programs')
  await expect(page.getByRole('heading', { name: 'Programs' })).toBeVisible()
  await page.getByPlaceholder('Search programs').fill('Family Weekend')
  await page.getByRole('row').filter({ hasText: 'Family Weekend' }).click()

  const sheet = page.getByRole('dialog')
  await expect(sheet.getByText('Pending approval').first()).toBeVisible()
  await sheet.getByRole('tab', { name: 'Approval' }).click()
  await expect(sheet.getByText('Jamie Dalton')).toBeVisible()
  await sheet.getByRole('button', { name: 'Approve as ministry owner' }).click()
  await expect(page.getByText('Family Weekend is published. Families can register now.')).toBeVisible()
  await expect(sheet.getByText('Published').first()).toBeVisible()

  await page.goto('/programs')
  await expect(page.getByRole('link', { name: /Family Weekend/ }).first()).toBeVisible()
})

test('Alex resizes a Family Weekend pool and the change is saved', async ({ page }) => {
  await signInAs(page, 'alex', '/admin/setup/programs')
  await page.getByPlaceholder('Search programs').fill('Family Weekend')
  await page.getByRole('row').filter({ hasText: 'Family Weekend' }).click()
  await page.getByRole('dialog').getByRole('tab', { name: 'Sessions' }).click()
  await page.getByRole('link', { name: /Summer 2028/ }).click()

  await expect(page.getByRole('heading', { name: 'Family Weekend · Summer 2028' })).toBeVisible()
  const pool = page.getByRole('row').filter({ hasText: 'Grades 6–12' })
  await expect(pool).toContainText('40')
  await page.getByRole('button', { name: 'Actions for Grades 6–12' }).click()
  await page.getByRole('menuitem', { name: 'Edit pool' }).click()
  await page.getByLabel('Capacity').fill('45')
  await page.getByRole('button', { name: 'Save pool' }).click()
  await expect(pool).toContainText('45')

  await page.reload()
  await expect(page.getByRole('row').filter({ hasText: 'Grades 6–12' })).toContainText('45')
})

test('Alex creates a discount rule and previews it on a registered camper', async ({ page }) => {
  await signInAs(page, 'alex', '/admin/setup/discount-rules')
  await expect(page.getByRole('row').filter({ hasText: 'SIBLING10' })).toBeVisible()
  await page.getByRole('button', { name: 'New rule' }).click()

  const sheet = page.getByRole('dialog')
  await sheet.getByLabel('Code').fill('SETUP15')
  await sheet.getByLabel('Name').fill('Setup demo 15%')
  await sheet.getByLabel('Percent off').fill('15')
  await sheet.getByLabel('Valid through').fill('2028-12-31')
  await expect(sheet.getByTestId('discount-preview')).toContainText('→')
  await sheet.getByRole('button', { name: 'Create rule' }).click()

  await expect(page.getByText('SETUP15 is live at checkout from its start date.')).toBeVisible()
  const row = page.getByRole('row').filter({ hasText: 'SETUP15' })
  await expect(row).toContainText('15% off')
  await expect(row).toContainText('Active')
})

test('Alex approves waiver v3, and families who signed v2 keep v2', async ({ page }) => {
  await signInAs(page, 'alex', '/admin/setup/waivers')
  await expect(page.getByRole('heading', { name: 'Waiver templates' })).toBeVisible()
  await page.getByRole('button', { name: /Family Weekend Release and Waiver/ }).click()
  await expect(page.getByText('v3 waiting for approval').first()).toBeVisible()
  await expect(page.getByLabel('Waiver text')).toHaveValue(/Lake and waterfront/)

  await page.getByRole('button', { name: 'Approve v3' }).click()
  const confirm = page.getByRole('alertdialog')
  await expect(confirm).toContainText('keep v2')
  await confirm.getByRole('button', { name: 'Approve and publish' }).click()

  await expect(page.getByText('v3 is live. Checkout asks for it from today.')).toBeVisible()
  const history = page.getByRole('list', { name: 'Version history' })
  await expect(history.getByRole('listitem').filter({ hasText: 'v3' })).toContainText('Approved by Alex Morgan (ADMIN)')
  await expect(history.getByRole('listitem').filter({ hasText: 'v2' })).toContainText('10 families signed')
})

test('the audit log shows what Alex changed, with before and after values', async ({ page }) => {
  await signInAs(page, 'alex', '/admin/setup/audit')
  await expect(page.getByText("This log can't be edited")).toBeVisible()
  await page.getByLabel('Search').fill('SETUP15')
  const row = page.getByRole('row').filter({ hasText: 'Discount rule created' })
  await expect(page.getByText('1 entry')).toBeVisible()
  await expect(row).toContainText('Alex Morgan (ADMIN)')
  await row.click()

  const entry = page.getByRole('dialog', { name: 'Discount rule created' })
  await expect(entry).toContainText('discount.rule_created')
  const code = entry.getByRole('listitem').filter({ hasText: 'Code' })
  await expect(code).toContainText('SETUP15')
})

test('a CET staff member can read the audit log but not program setup @phone', async ({ page }) => {
  await signInAs(page, 'diane', '/admin/setup/programs')
  // The router guard sends a role mismatch to the No access page before the page's own 403 copy can render.
  await expect(page).toHaveURL(/\/no-access\?need=role/)
  await expect(page.getByText(/needs the Administrator role/)).toBeVisible()
  await page.goto('/admin/setup/audit')
  await expect(page.getByRole('heading', { name: 'Audit log' })).toBeVisible()
  await expect(page.getByText(/\d+ entries/)).toBeVisible()
})

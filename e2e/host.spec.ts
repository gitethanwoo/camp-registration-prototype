import { fileURLToPath } from 'node:url'
import { expect, signInAs, test } from './fixtures'

// Slice 8 demo on fresh seed data: Grace Patel, host coordinator for Grace Community Church, lands on
// the host home, uploads the 40-row volunteer CSV (37 valid, rows 8, 17 and 32 with errors), fixes row
// 8, skips row 17, submits 38 to vetting while row 32 stays held back, then pays the open invoice after
// a declined card. Submitting and paying change data for good, so the spec needs a freshly seeded
// database.
const csv = fileURLToPath(new URL('./fixtures/grace-volunteers.csv', import.meta.url))

test.describe.configure({ mode: 'serial' })

test('Grace signs in and lands on the host home', async ({ page }) => {
  await signInAs(page, 'grace')
  await expect(page).toHaveURL(/\/host$/)
  await expect(page.getByRole('heading', { name: 'Day Camp host overview' })).toBeVisible()
  await expect(page.getByText('Grace Community Church · Atlanta, GA · June 12–16, 2028')).toBeVisible()
  await expect(page.getByTestId('registrations')).toHaveText(/\d+ \/ 120/)
  await expect(page.getByTestId('invoice-balance')).toHaveText('$1,200 due')
  const deadlines = page.getByTestId('deadlines')
  await expect(deadlines.getByText('Vetting done by May 15, 2028')).toBeVisible()
  await expect(deadlines.getByText('Pay INV-2028-041 by May 20, 2028')).toBeVisible()

  // The host has no staff console.
  await page.goto('/admin')
  await expect(page).toHaveURL(/\/host$/)
})

test('Grace uploads the CSV, fixes one row, skips one, and submits the valid rows', async ({ page }) => {
  await signInAs(page, 'grace', '/host/volunteers')
  await expect(page.getByRole('heading', { name: 'Volunteers', exact: true })).toBeVisible()
  await expect(page.getByTestId('volunteer-range')).toHaveText('1–10 of 60 volunteers')

  await page.getByTestId('csv-input').setInputFiles(csv)
  const preview = page.getByTestId('upload-preview')
  await expect(preview.getByTestId('upload-counts')).toHaveText('40 rows · 37 valid · 3 errors')
  await expect(preview.getByText('Nothing is dropped silently; 3 rows need a decision.')).toBeVisible()
  await expect(preview.getByTestId('error-row-8')).toContainText('Missing email')
  await expect(preview.getByTestId('error-row-17')).toContainText('Invalid date of birth')
  await expect(preview.getByTestId('error-row-32')).toContainText('Duplicate email')

  // Nothing hides the open rows behind a second file.
  await page.getByTestId('csv-input').setInputFiles(csv)
  await expect(page.getByText(/Finish your open upload first: 40 rows still need/)).toBeVisible()

  // Row 8: a typo is caught on save, then the real address clears it.
  await preview.getByTestId('error-row-8').getByRole('button', { name: 'Fix' }).click()
  const dialog = page.getByRole('dialog', { name: 'Fix row 8' })
  await expect(dialog.getByText('Missing email')).toBeVisible()
  await dialog.getByLabel('Email').fill('chris.walker@example')
  await dialog.getByRole('button', { name: 'Save row' }).click()
  await expect(dialog.getByText('Invalid email')).toBeVisible()
  await dialog.getByLabel('Email').fill('chris.walker@example.org')
  await dialog.getByRole('button', { name: 'Save row' }).click()
  await expect(dialog).toBeHidden()
  await expect(preview.getByTestId('upload-counts')).toHaveText('40 rows · 38 valid · 2 errors')

  await preview.getByTestId('error-row-17').getByRole('button', { name: 'Skip' }).click()
  await expect(preview.getByTestId('skipped-row-17')).toBeVisible()
  await expect(preview.getByTestId('upload-counts')).toHaveText('40 rows · 38 valid · 1 error · 1 skipped')

  await preview.getByRole('button', { name: 'Submit 38 valid to vetting' }).click()
  await expect(page.getByText('38 volunteers sent to vetting. 1 row held back.')).toBeVisible()
  await expect(preview.getByTestId('upload-counts')).toHaveText(
    '40 rows · 0 valid · 1 error · 1 skipped · 38 sent to vetting',
  )
  await expect(preview.getByTestId('error-row-32')).toBeVisible()
  await expect(preview.getByTestId('held-back-note')).toContainText('1 row still held back')

  // The list now has the 38, each waiting on a background check.
  await expect(page.getByTestId('volunteer-range')).toHaveText('1–10 of 98 volunteers')
  await page.getByPlaceholder('Search volunteers…').fill('chris.walker')
  await expect(page.getByRole('cell', { name: 'Not started' })).toBeVisible()

  await page.getByRole('link', { name: 'Home' }).click()
  await expect(page.getByTestId('deadlines').getByText('Fix 1 volunteer CSV row')).toBeVisible()
})

test('Grace pays the open invoice after a declined card', async ({ page }) => {
  await signInAs(page, 'grace', '/host/invoices')
  await expect(page.getByRole('heading', { name: 'Invoices & payments' })).toBeVisible()
  const detail = page.getByTestId('invoice-detail')
  await expect(detail.getByText('Invoice INV-2028-041')).toBeVisible()
  await expect(detail.getByText('Balance due')).toBeVisible()
  await expect(detail.getByRole('row', { name: 'Host participation fee $800' })).toBeVisible()
  await expect(detail.getByRole('row', { name: 'Site services $400' })).toBeVisible()
  await expect(detail.getByRole('row', { name: 'Total $1,200' })).toBeVisible()

  await detail.getByRole('button', { name: '4000 0000 0000 0002' }).click()
  await detail.getByRole('button', { name: 'Pay $1,200' }).click()
  await expect(detail.getByTestId('pay-declined')).toContainText('Payment declined; invoice remains due.')
  await expect(detail.getByText('Balance due')).toBeVisible()

  await detail.getByRole('button', { name: '4242 4242 4242 4242' }).click()
  await detail.getByRole('button', { name: 'Pay $1,200' }).click()
  await expect(page.getByText('$1,200 paid on card ending 4242.')).toBeVisible()
  await expect(detail.getByTestId('invoice-paid')).toBeVisible()
  await expect(page.getByRole('row', { name: /INV-2028-041.*Paid/ })).toBeVisible()

  await page.getByRole('link', { name: 'Home' }).click()
  await expect(page.getByTestId('invoice-balance')).toHaveText('Paid up')
})

test('the host portal fits a phone @phone', async ({ page }) => {
  await signInAs(page, 'grace', '/host/volunteers')
  await expect(page.getByRole('heading', { name: 'Volunteers', exact: true })).toBeVisible()
  const noSideScroll = () => page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)
  await expect.poll(noSideScroll).toBe(true)
  // Below 1024px the list and the upload preview sit behind tabs.
  if ((page.viewportSize()?.width ?? 0) < 1024) {
    await page.getByRole('tab', { name: /Volunteer list/ }).click()
    await expect(page.getByTestId('volunteer-range')).toBeVisible()
    await page.getByRole('tab', { name: /Upload preview/ }).click()
    await expect(page.getByRole('tabpanel', { name: /Upload preview/ })).toBeVisible()
    await expect.poll(noSideScroll).toBe(true)
  }

  await page.goto('/host/invoices')
  await expect(page.getByTestId('invoice-detail')).toBeVisible()
  await expect.poll(noSideScroll).toBe(true)
  await page.goto('/host')
  await expect(page.getByRole('heading', { name: 'Day Camp host overview' })).toBeVisible()
  await expect.poll(noSideScroll).toBe(true)
})

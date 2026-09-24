import { expect, signInAs, test } from './fixtures'

// Slice 6 demo path (FN1–FN4, O6, O7) against a fresh seed. Tests run in file order and build on each other:
// Maria's application is the one Marcus reviews.

// A tiny valid PDF: the api checks the file's first bytes, not its name.
const pdf = Buffer.from('%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF\n')

test('Maria applies for financial assistance for Family Camp (O6)', async ({ page }) => {
  await signInAs(page, 'maria', '/family')
  await page.getByRole('button', { name: /Maria Johnson/ }).click()
  await page.getByRole('menuitem', { name: 'Scholarships' }).click()
  await expect(page.getByRole('heading', { name: 'Scholarships', level: 1 })).toBeVisible()

  await page
    .locator('[data-slot="card"]')
    .filter({ hasText: 'Family Camp' })
    .getByRole('link', { name: 'Apply for assistance' })
    .click()
  await expect(page.getByRole('heading', { name: 'Apply for financial assistance' })).toBeVisible()
  await expect(page.getByText('Maria Johnson').first()).toBeVisible()

  // Submitting empty shows each missing field, including the document.
  await page.getByRole('button', { name: 'Submit application' }).click()
  await expect(page.getByText('A supporting document is required')).toBeVisible()
  await expect(page.getByText('Tell us a little about your situation.')).toBeVisible()

  await page
    .getByLabel('Reason for requesting assistance')
    .fill('A job change this spring left us short for summer camp.')
  await page.getByLabel('Requested assistance amount ($)').fill('400')
  await page.getByRole('combobox', { name: 'Household income band' }).click()
  await page.getByRole('option', { name: '$35,000–$50,000' }).click()
  await page
    .getByLabel('Supporting document')
    .setInputFiles({ name: 'pay-stub.pdf', mimeType: 'application/pdf', buffer: pdf })
  await expect(page.getByTestId('file-name')).toHaveText('pay-stub.pdf')
  await page.getByRole('button', { name: 'Submit application' }).click()

  await expect(page).toHaveURL(/\/family\/scholarships$/)
  await expect(page.getByText('Application submitted.')).toBeVisible()
  const card = page.locator('[data-testid^="application-"]').filter({ hasText: 'WS-FC2J08' })
  await expect(card.getByText('In review')).toBeVisible()
  await expect(card.getByText('$400')).toBeVisible()
})

test('Marcus is stopped by the scholarship cap, then approves an award that lowers the balance (O7)', async ({
  page,
}) => {
  await signInAs(page, 'marcus', '/admin/scholarships')
  await page.getByRole('row', { name: /Johnson family/ }).click()
  const sheet = page.getByRole('dialog')
  await expect(sheet.getByText('pay-stub.pdf')).toBeVisible()
  await expect(sheet.getByTestId('eligible')).toHaveText('$950')

  const award = sheet.getByLabel('Award amount ($)')
  await award.fill('1000')
  await expect(sheet.getByTestId('cap-check')).toContainText(
    'Cannot approve: award $1,000 exceeds $950 eligible total; scholarships plus discounts cannot exceed 100%.',
  )
  await expect(sheet.getByRole('button', { name: /^Approve/ })).toBeDisabled()

  await award.fill('300')
  await expect(sheet.getByTestId('cap-check')).toBeHidden()
  await expect(sheet.getByText('Balance after award: $450')).toBeVisible()
  await sheet.getByLabel('Note for the file').fill('Income documented; standard award.')
  await sheet.getByRole('button', { name: 'Approve $300' }).click()
  await expect(page.getByText('Approved $300 for the Johnson family.')).toBeVisible()
  await expect(page.getByText('Their balance is now $450.')).toBeVisible()
  await expect(page.getByRole('row', { name: /Johnson family/ })).toBeHidden()

  await page.getByRole('tab', { name: 'Approved' }).click()
  await expect(page.getByRole('row', { name: /Johnson family/ })).toContainText('Approved')
})

test('Maria sees the award on her application', async ({ page }) => {
  await signInAs(page, 'maria', '/family/scholarships')
  const card = page.locator('[data-testid^="application-"]').filter({ hasText: 'WS-FC2J08' })
  await expect(card.getByText('Approved')).toBeVisible()
  await expect(card.getByText('Awarded $300. It has been applied to your balance.')).toBeVisible()
})

test('Marcus resolves both unmatched settlement lines and the journal is created (FN2 → FN4)', async ({ page }) => {
  await signInAs(page, 'marcus', '/admin/finance/reconciliation')
  await expect(page.getByTestId('unmatched-count')).toContainText('2 unmatched')
  await expect(page.getByTestId('journal-status')).toContainText('Not created')

  await page.getByRole('button', { name: 'View unmatched only' }).click()
  await page.getByRole('row', { name: /Dana Mitchell/ }).click()
  const sheet = page.getByRole('dialog')
  await expect(sheet.getByRole('heading', { name: 'Resolve transaction' })).toBeVisible()
  await expect(sheet.getByRole('radio', { name: /WS-FC3M17/ })).toBeChecked()
  await sheet.getByLabel('Audit note').fill('Phone payment taken by the front desk; confirmed with Dana.')
  await sheet.getByRole('button', { name: 'Resolve and match' }).click()
  await expect(page.getByText("$250 matched to WS-FC3M17. The family's balance is updated.")).toBeVisible()
  await expect(page.getByTestId('unmatched-count')).toContainText('1 unmatched')

  await page.getByRole('row', { name: /K\. Nguyen/ }).click()
  await expect(sheet.getByText("No registration with this cardholder's name owes this amount.")).toBeVisible()
  await sheet.getByText('None of these match').click()
  await sheet.getByRole('button', { name: 'Resolve as adjustment' }).click()
  await expect(sheet.getByText('Add an audit note saying how you know.')).toBeVisible()
  await sheet.getByLabel('Audit note').fill('No registration found; holding in unapplied receipts pending contact.')
  await sheet.getByRole('button', { name: 'Resolve as adjustment' }).click()
  await expect(page.getByText('$125 posted to unapplied receipts.')).toBeVisible()
  await expect(page.getByTestId('journal-status')).toContainText('Pending')
  await expect(page.getByTestId('unmatched-count')).toContainText('0 unmatched')

  await page.getByRole('link', { name: 'Oracle exports' }).click()
  await expect(page.getByRole('row', { name: /JRN-\d{4}-\d{2}-\d{2}/ }).first()).toContainText('Pending')
  await expect(page.getByText('is waiting on reconciliation')).toBeHidden()
})

test('Marcus retries failed installments: one charges, one declines again (FN3)', async ({ page }) => {
  await signInAs(page, 'marcus', '/admin/finance/plan-exceptions')
  await expect(page.getByRole('row', { name: /Garcia family/ })).toContainText('Installment failed')

  await page.getByRole('row', { name: /Garcia family/ }).click()
  const sheet = page.getByRole('dialog')
  await expect(sheet.getByText(/Grace period ends in \d+ days?/)).toBeVisible()
  await sheet.getByRole('button', { name: 'Retry now' }).click()
  await expect(page.getByText(/Garcia family: \$125 charged to .* The plan is back on track\./)).toBeVisible()
  await expect(page.getByRole('row', { name: /Garcia family/ })).toBeHidden()

  await page.getByRole('row', { name: /Brennan family/ }).click()
  await sheet.getByRole('button', { name: 'Retry now' }).click()
  await expect(page.getByText('Brennan family: card declined again.')).toBeVisible()
  await expect(sheet.getByText(/The card was declined again/)).toBeVisible()

  await sheet.getByRole('button', { name: 'Contact family' }).click()
  await expect(page.getByText(/Email queued to /)).toBeVisible()
})

test('Marcus retries a failed Oracle Fusion export (FN4)', async ({ page }) => {
  await signInAs(page, 'marcus', '/admin/finance/journals')
  await page.getByRole('tab', { name: /Failed/ }).click()
  const failed = page.getByRole('row', { name: /Failed/ }).first()
  const reference = (await failed.getByRole('cell').nth(1).textContent())?.trim() ?? ''
  await failed.click()
  const sheet = page.getByRole('dialog')
  await expect(sheet.getByText('Export failed')).toBeVisible()
  await expect(sheet.getByText('Debits and credits are balanced.')).toBeVisible()
  await sheet.getByRole('button', { name: 'Retry export' }).click()
  await expect(page.getByText(`${reference} resent to Oracle Fusion.`)).toBeVisible()
  await expect(sheet.getByText('Export failed')).toBeHidden()
  await expect(sheet.getByRole('button', { name: 'Retry export' })).toBeHidden()
})

test('Settled revenue is certified, ties to Fiserv batches, and exports as CSV (FN1)', async ({ page }) => {
  await signInAs(page, 'marcus', '/admin/finance/reports')
  await expect(page.getByTestId('certified')).toHaveText('Certified metric')
  await expect(page.getByTestId('tie-out')).toContainText(/Ties to \d+ Fiserv settlement batches/)
  await expect(page.getByRole('row', { name: /Family Camp/ }).first()).toBeVisible()

  const download = page.waitForEvent('download')
  await page.getByRole('link', { name: 'Export report' }).click()
  const file = await download
  const stream = await file.createReadStream()
  const chunks: Buffer[] = []
  for await (const chunk of stream) chunks.push(chunk as Buffer)
  const csv = Buffer.concat(chunks).toString('utf8')
  expect(csv.split('\n')[0]).toBe('Program,Session,Registrations,Attended,Settled revenue,Contracted tuition')
  expect(csv).toContain('Family Camp')
})

test('the family scholarship pages work at phone width @phone', async ({ page }) => {
  await signInAs(page, 'maria', '/family/scholarships')
  await expect(page.getByRole('heading', { name: 'Scholarships', level: 1 })).toBeVisible()
  const width = await page.evaluate(() => document.documentElement.scrollWidth)
  expect(width).toBeLessThanOrEqual(page.viewportSize()?.width ?? 0)
})

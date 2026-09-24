import type { Page } from '@playwright/test'
import { expect, signInAs, test } from './fixtures'

// Slice 1 demo on fresh seed data: Sam signs in for the first time, adds a child, and registers
// them for Day Camp. Maria registers Avery and Mia on the payment plan and pays her balance.
// Tests in this file run in order (fullyParallel: false) and each one leaves data the next can use.

/** Goes through the existing registration wizard for Day Camp and lands on the confirmation. */
async function registerForDayCamp(page: Page, kids: string[], option: 'Pay deposit' | 'Payment plan') {
  await page.goto('/programs/day-camp-atlanta')
  // Day Camp has two sessions in the combined seed (staff-cx adds June week 2); the demo uses the first.
  await page.getByRole('button', { name: 'Register for June week', exact: true }).click()
  await expect(page).toHaveURL(/\/register\/\d+/)

  for (const kid of kids) await page.getByRole('checkbox', { name: `Register ${kid}` }).click()
  await page.getByRole('button', { name: 'Continue' }).click()

  for (const kid of kids) {
    const card = page.locator('[data-slot="card"]').filter({ hasText: `About ${kid}` })
    await card.getByRole('combobox', { name: /T-shirt size/ }).click()
    await page.getByRole('option', { name: 'Youth M' }).click()
    await card.getByRole('combobox', { name: /Swimming ability/ }).click()
    await page.getByRole('option', { name: 'Beginner' }).click()
  }
  // Each camper card now has its own yes/no (the forms slice's medication question), so answer the family's.
  await page
    .locator('[data-slot="card"]')
    .filter({ hasText: 'About your family' })
    .getByRole('radio', { name: 'No' })
    .click()
  await page.getByRole('button', { name: 'Continue' }).click()

  const physicians = page.getByLabel(/Physician name/)
  await expect(physicians).toHaveCount(kids.length)
  for (let i = 0; i < kids.length; i++) await physicians.nth(i).fill('Dr. Adams')
  await page.getByRole('button', { name: 'Continue' }).click()

  const agree = page.getByRole('checkbox', { name: /I agree/ })
  await expect(agree.first()).toBeVisible()
  for (const box of await agree.all()) await box.click()
  const signer = page.getByLabel('Full name')
  if (!(await signer.inputValue())) await signer.fill('Parent Signature')
  await page.getByRole('button', { name: 'Continue' }).click()

  await page.getByRole('radio', { name: option }).click()
  if (option === 'Payment plan') {
    // The demo clock says Mar 2, 2028: every installment is still ahead, three of them, the last on Jun 1.
    await expect(page.getByRole('row', { name: /Installment 1 of 3/ })).toContainText('Apr 1, 2028')
    await expect(page.getByRole('row', { name: /Installment 3 of 3/ })).toContainText('Jun 1, 2028')
    await expect(page.getByText('Mar 1, 2028')).toHaveCount(0)
  }
  await page.getByRole('button', { name: 'Continue to payment' }).click()
  await page.getByRole('button', { name: '4242 4242 4242 4242' }).click()
  await page.getByRole('button', { name: /^Pay \$/ }).click()
  await expect(page.getByRole('heading', { name: 'You’re registered!' })).toBeVisible()
  if (option === 'Payment plan') {
    await expect(page.getByText('Apr 1, 2028')).toBeVisible()
    await expect(page.getByText('Jun 1, 2028')).toBeVisible()
  }
}

test('Sam signs in for the first time, adds a child, and registers them for Day Camp', async ({ page }) => {
  await signInAs(page, 'sam', '/family')
  await expect(page.getByRole('heading', { name: 'My family' })).toBeVisible()
  await expect(page.getByText('Register for a program and your to-dos show up here.')).toBeVisible()

  await page.getByRole('link', { name: 'Add your first child' }).click()
  await expect(page.getByRole('heading', { name: 'Add a child' })).toBeVisible()
  await page.getByLabel('First name').fill('Leo')
  await expect(page.getByLabel('Last name')).toHaveValue('Rivera')
  await page.getByLabel('Date of birth').fill('2020-02-14')
  await expect(page.getByLabel('Grade in fall 2028')).toHaveValue('Grade 3')
  await page.getByRole('combobox', { name: 'Boy or girl' }).click()
  await page.getByRole('option', { name: 'Boy' }).click()
  await page.getByLabel('Allergies').fill('Bee stings')
  await page.getByRole('button', { name: 'Add child' }).click()

  await expect(page).toHaveURL(/\/family$/)
  await expect(page.getByText('Leo added · Grade 3 in fall 2028')).toBeVisible()
  const leo = page.getByRole('link', { name: /Leo Rivera/ })
  await expect(leo).toContainText('Grade 3')

  await registerForDayCamp(page, ['Leo'], 'Pay deposit')

  await page.goto('/family')
  await expect(page.getByRole('heading', { name: 'Day Camp' })).toBeVisible()
  const balance = page.locator('#checklist li').filter({ hasText: 'Balance due' })
  await expect(balance).toContainText('$100 paid · $225 due by Jun 1, 2028')
  await expect(balance.getByRole('link', { name: 'Pay balance' })).toHaveAttribute(
    'href',
    /\/family\/registrations\/WS-[A-Z0-9]+\/payments$/,
  )

  // His profile now explains why date of birth is locked.
  await leo.click()
  await expect(
    page.getByText(/Leo is registered for Day Camp · Atlanta, so date of birth and gender are locked/),
  ).toBeVisible()
  await expect(page.getByText('Changes save automatically')).toBeVisible()
  await expect(page.getByLabel('Date of birth')).toBeDisabled()
  await expect(page.getByLabel('Allergies')).toHaveValue('Bee stings')
  await page.getByLabel('Dietary needs').fill('No pork')
  await expect(page.getByText('Saved just now')).toBeVisible()
  await page.reload()
  await expect(page.getByLabel('Dietary needs')).toHaveValue('No pork')
})

test('Maria registers Avery and Mia on the plan, a declined card changes nothing, then she pays the balance', async ({
  page,
}) => {
  await signInAs(page, 'maria', '/family')
  await expect(page.getByRole('link', { name: /Avery Johnson/ })).toContainText('Grade 6')
  await expect(page.getByRole('link', { name: /Mia Johnson/ })).toContainText('Grade 4')

  await registerForDayCamp(page, ['Avery', 'Mia'], 'Payment plan')

  // F1: one checklist across both kids, and the plan's progress.
  await page.goto('/family')
  await expect(page.getByText('$200 of $650 total')).toBeVisible()
  await expect(page.getByText(/3 payments of \$150/)).toBeVisible()
  const plan = page.locator('#checklist li').filter({ hasText: 'Payment plan' })
  await expect(plan).toContainText('Avery and Mia')
  await expect(plan).toContainText('$450 remaining')

  // F4 → F5: the new registration is upcoming, the 2026 retreat is past. The admittance spec may
  // already have added Maria's fall retreat to Upcoming, so check each section's own cards.
  await page.goto('/family/registrations')
  const upcoming = page.getByRole('region', { name: /^Upcoming/ })
  const past = page.getByRole('region', { name: /^Past/ })
  await expect(upcoming.getByRole('link', { name: /View details for Day Camp · Atlanta/ })).toHaveCount(1)
  await expect(upcoming).toContainText('3 × $150 payment plan')
  await expect(upcoming).not.toContainText('Spring Marriage Retreat')
  await expect(page.getByRole('heading', { name: 'Past (1)' })).toBeVisible()
  await expect(past).toContainText('Spring Marriage Retreat')
  await expect(page.getByRole('heading', { name: 'Cancelled (0)' })).toBeVisible()
  await upcoming.getByRole('link', { name: /View details for Day Camp · Atlanta/ }).click()
  await expect(page.getByRole('heading', { name: 'Day Camp · Atlanta registration' })).toBeVisible()
  await expect(page.getByText('Avery and Mia Johnson')).toBeVisible()
  await expect(page.locator('#checklist')).toContainText('Avery · Waivers (3 signed)')
  // Two campers on the order, so "Request transfer" opens F8's list to pick one.
  await expect(page.getByRole('link', { name: 'Request transfer' })).toHaveAttribute('href', '/family/transfers')

  // F6: pay the balance. The declined card leaves it at $450.
  await page.getByRole('link', { name: 'Payments and balance' }).click()
  await expect(page.getByRole('heading', { name: 'Day Camp · Atlanta payments' })).toBeVisible()
  await expect(page.getByText('Payment plan: 3 × $150, last one Jun 1, 2028')).toBeVisible()
  await expect(page.getByText('Next: $150 on Apr 1, 2028', { exact: false })).toBeVisible()
  await page.getByRole('button', { name: 'Pay $450 now' }).click()
  const dialog = page.getByRole('dialog', { name: 'Pay balance' })
  await dialog.getByRole('button', { name: '4000 0000 0000 0002' }).click()
  await dialog.getByRole('button', { name: 'Pay $450' }).click()
  await expect(dialog.getByRole('alert')).toContainText('Your balance hasn’t changed.')

  await dialog.getByRole('button', { name: '4242 4242 4242 4242' }).click()
  await dialog.getByRole('button', { name: 'Pay $450' }).click()
  await expect(dialog).toBeHidden()
  await expect(page.getByText('$450 paid with card ending 4242')).toBeVisible()
  await expect(page.getByText('Paid in full')).toBeVisible()
  await expect(page.getByText('Balance payment · $450')).toBeVisible()

  await page.getByRole('button', { name: /Receipt for Balance payment/ }).click()
  const receipt = page.getByRole('dialog', { name: 'Receipt' })
  await expect(receipt).toContainText('Card ending 4242')
  await expect(receipt).toContainText('Balance remaining today')
  await page.keyboard.press('Escape')

  await page.goto('/family')
  await expect(page.locator('#checklist')).toContainText('Paid in full · $650')
})

test('Maria sees who has access and that revoking keeps guardian links', async ({ page }) => {
  await signInAs(page, 'maria', '/family/access')
  await expect(page.getByRole('heading', { name: 'Household access' })).toBeVisible()
  await expect(page.getByText('Primary owner · maria.johnson@example.com')).toBeVisible()
  await page.getByRole('button', { name: 'Actions for David' }).click()
  await page.getByRole('menuitem', { name: 'Revoke access' }).click()
  const confirm = page.getByRole('alertdialog')
  await expect(confirm).toContainText('Revoking account access does not remove guardian links.')
  await confirm.getByRole('button', { name: 'Cancel' }).click()
  await expect(page.getByText('Co-owner · david.johnson@example.com')).toBeVisible()
})

test('a family checks their registrations on a phone @phone', async ({ page }) => {
  await signInAs(page, 'maria', '/family/registrations')
  await expect(page.getByRole('heading', { name: /Past \(1\)/ })).toBeVisible()
  await expect(page.getByText('Spring Marriage Retreat')).toBeVisible()
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth)
  expect(overflow).toBeLessThanOrEqual(0)

  await page.goto('/family')
  await expect(page.getByRole('heading', { name: 'My family' })).toBeVisible()
  expect(await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth)).toBeLessThanOrEqual(0)
})

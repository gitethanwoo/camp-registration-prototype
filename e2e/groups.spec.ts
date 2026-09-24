import type { Page } from '@playwright/test'
import { expect, signInAs, test } from './fixtures'

// Slice 3 · groups and cohorts. Pastor Dave registers a church group for the Emerging Leaders
// Cohort (R8), each attendee completes their own forms by secure link (G1), and Dave tracks
// completion and handles a withdrawal from his tracker (G2).

test('a group leader registers a roster, an attendee completes forms by link, and a withdrawal is refunded', async ({
  page,
  browser,
}) => {
  const run = Date.now().toString(36)
  const first = `Priya Nair ${run}`
  const second = `Owen Brooks ${run}`
  const third = `Grace Holt ${run}`

  // R8 · start from the cohort program page.
  await signInAs(page, 'pastorDave', '/programs/emerging-leaders-cohort')
  await page.getByRole('button', { name: 'Register your group' }).click()
  await expect(page.getByRole('heading', { name: 'Attendee roster' })).toBeVisible()
  await expectNoSideScroll(page)

  await page.getByLabel('Group name').fill(`Youth leaders ${run}`)
  await page.getByLabel('Attendee 1 name').fill(first)
  await page.getByLabel('Attendee 1 email').fill(`priya.${run}@example.com`)
  await page.getByRole('button', { name: 'Add attendee' }).click()
  await page.getByLabel('Attendee 2 name').fill(second)
  await page.getByLabel('Attendee 2 email').fill(`owen.${run}@example.com`)
  await page.getByRole('button', { name: 'Add attendee' }).click()
  await page.getByLabel('Attendee 3 name').fill(third) // no email yet
  await expect(page.getByText("No email yet: they won't get a link until you add one.")).toBeVisible()
  await expect(page.getByText('$1,350')).toBeVisible()

  await page.getByRole('button', { name: 'Continue to payment' }).click()
  await expect(page.getByRole('heading', { name: 'Review and pay' })).toBeVisible()
  await expect(page.getByText('1 attendee has no email')).toBeVisible()

  // A declined card keeps the roster and charges nothing; the next card goes through.
  await page.getByRole('button', { name: '4000 0000 0000 0002' }).click()
  await page.getByRole('button', { name: 'Pay $1,350' }).click()
  await expect(page.getByText('Payment declined')).toBeVisible()
  await page.getByRole('button', { name: '4242 4242 4242 4242' }).click()
  await page.getByRole('button', { name: 'Pay $1,350' }).click()

  // G2 · the tracker.
  await expect(page).toHaveURL(/\/groups\/\d+$/)
  await expect(page.getByRole('heading', { name: 'Attendee completion' })).toBeVisible()
  await expect(page.getByTestId('kpi-attendees')).toHaveText('3')
  await expect(page.getByTestId('kpi-complete')).toHaveText('0')
  await expect(page.getByText("You can't complete forms on their behalf")).toBeVisible()

  await page.getByRole('button', { name: `Actions for ${first}` }).click()
  await page.getByRole('menuitem', { name: 'Resend link' }).click()
  const dialog = page.getByRole('dialog', { name: 'Link sent' })
  await expect(dialog).toBeVisible()
  const link = ((await dialog.locator('code').textContent()) ?? '').trim()
  expect(link).toMatch(/\/g\/[\w-]+$/)
  await dialog.getByRole('button', { name: 'Done' }).click()

  // G1 · the attendee opens the link with no account.
  const guest = await browser.newContext()
  const attendee = await guest.newPage()
  await attendee.goto(link)
  await expect(
    attendee.getByRole('heading', {
      name: 'Completing forms for Emerging Leaders Cohort',
    }),
  ).toBeVisible()
  await expect(attendee.getByText('Dave Kim registered you for the Emerging Leaders Cohort')).toBeVisible()
  await expect(attendee.getByText(second)).toHaveCount(0) // sees only their own record

  await attendee.getByRole('button', { name: 'Submit forms' }).click()
  await expect(attendee.getByText('Emergency contact name is required.')).toBeVisible()

  await attendee.getByLabel('Emergency contact name').fill('Ravi Nair')
  await attendee.getByLabel('Emergency contact phone').fill('404-555-0199')
  await attendee.getByRole('combobox', { name: 'T-shirt size' }).click()
  await attendee.getByRole('option', { name: 'Adult M', exact: true }).click()
  await attendee.getByLabel('I have read and agree to the Participant Release and Waiver of Liability.').check()
  await attendee.getByLabel('I have read and agree to the Photo and Media Release.').check()
  await attendee.getByRole('button', { name: 'Submit forms' }).click()
  await expect(attendee.getByText("You're all set")).toBeVisible()

  await page.reload()
  await expect(page.getByTestId('kpi-complete')).toHaveText('1')
  await expect(page.getByTestId('kpi-incomplete')).toHaveText('2')

  // The attendee can't attend after all; Dave approves the refund.
  await attendee.getByRole('button', { name: "Can't attend? Request withdrawal" }).click()
  await attendee.getByLabel('Reason').fill('Work conflict that weekend')
  await attendee.getByRole('button', { name: 'Send request' }).click()
  await expect(attendee.getByText('Withdrawal requested')).toBeVisible()
  await guest.close()

  await page.reload()
  await expect(page.getByText(`${first} asked to withdraw`)).toBeVisible()
  await page.getByRole('button', { name: 'Approve refund' }).click()
  await page.getByRole('alertdialog').getByRole('button', { name: 'Approve refund' }).click()
  await expect(page.getByText(`${first} withdrawn. $450 refunded`)).toBeVisible()
  await expect(page.getByTestId('kpi-attendees')).toHaveText('2')
  await expect(page.getByText('Refunded', { exact: true })).toBeVisible()
  await expect(page.getByRole('heading', { name: 'Withdrawn' })).toBeVisible()
})

test('the seeded group shows its completion numbers and a pending withdrawal @phone', async ({ page }) => {
  await signInAs(page, 'pastorDave', '/groups')
  await page.getByRole('link', { name: /Northside Fellowship leaders/ }).click()
  await expect(page.getByRole('heading', { name: 'Attendee completion' })).toBeVisible()
  await expect(page.getByTestId('kpi-attendees')).toHaveText('14')
  await expect(page.getByTestId('kpi-complete')).toHaveText('9')
  await expect(page.getByTestId('kpi-incomplete')).toHaveText('5')
  await expect(page.getByText('Mateo Silva asked to withdraw')).toBeVisible()

  await page.getByRole('combobox', { name: 'Filter by status' }).click()
  await page.getByRole('option', { name: 'No email' }).click()
  await expect(page.getByText('Noah Bennett').filter({ visible: true })).toBeVisible()
  await expect(page.getByText('Alex Chen')).toHaveCount(0)
})

test('the roster page has no sideways scroll at phone width @phone', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 })
  await signInAs(page, 'pastorDave', '/programs/emerging-leaders-cohort')
  await page.getByRole('button', { name: 'Register your group' }).click()
  await expect(page.getByRole('heading', { name: 'Attendee roster' })).toBeVisible()
  await expectNoSideScroll(page)
})

async function expectNoSideScroll(page: Page) {
  const { scroll, client } = await page.evaluate(() => ({
    scroll: document.documentElement.scrollWidth,
    client: document.documentElement.clientWidth,
  }))
  expect(scroll).toBeLessThanOrEqual(client)
}

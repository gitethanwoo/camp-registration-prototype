import { expect, signInAs, test } from './fixtures'

// Demo path for admittance programs (R2 → F7 → C6 → F7). Needs fresh seed data: Maria has no
// retreat application until this test makes one.
test('a couple applies with the card held, CET approves, and the charge confirms their spot', async ({
  page,
  browser,
}) => {
  // R2: Maria applies from the retreat page.
  await signInAs(page, 'maria', '/programs/fall-marriage-retreat')
  await page.getByRole('button', { name: 'Apply', exact: true }).click()
  await expect(page.getByRole('heading', { name: 'Apply for Fall Marriage Retreat' })).toBeVisible()

  // Couple details: David is on the family account, so he's already picked.
  await expect(page.getByRole('combobox', { name: 'Your spouse' })).toContainText('David Johnson')
  await page.getByRole('button', { name: 'Save and continue' }).click()
  await expect(page.getByText('Please answer the following')).toBeVisible()
  await page.getByRole('combobox', { name: 'How long have you been married?' }).click()
  await page.getByRole('option', { name: '6–15 years' }).click()
  await page.getByRole('button', { name: 'Save and continue' }).click()

  await page.getByLabel('Why do you want to attend').fill('Ten years in, we want a weekend to reset together.')
  await page.getByLabel('What are your goals').fill('Listen better and plan our next season.')
  await page.getByRole('button', { name: 'Save and continue' }).click()

  await page.getByRole('combobox', { name: 'How did you hear about the retreat?' }).click()
  await page.getByRole('option', { name: 'Our church' }).click()
  await page.getByRole('radio', { name: 'No' }).click()
  await page.getByRole('button', { name: 'Save and continue' }).click()
  await expect(page.getByText(/Draft saved/)).toBeVisible()

  // Review & submit: the card is authorized, not charged.
  await expect(page.getByRole('main').getByText('Maria and David Johnson').first()).toBeVisible()
  await expect(page.getByText('Your card will be authorized, not charged')).toBeVisible()
  await page.getByRole('button', { name: '4242 4242 4242 4242' }).click()
  await page.getByRole('button', { name: 'Authorize $900 and submit' }).click()

  // F7: status shows the hold, not a charge.
  await expect(page).toHaveURL(/\/applications\/\d+$/)
  await expect(page.getByRole('heading', { name: 'Fall Marriage Retreat application' })).toBeVisible()
  await expect(page.getByText('Application pending')).toBeVisible()
  await expect(page.getByText('Authorized (not charged)')).toBeVisible()
  await expect(page.getByText('No action needed while we review.')).toBeVisible()

  // C6: Diane finds the application and approves it.
  const staff = await browser.newContext()
  const desk = await staff.newPage()
  await signInAs(desk, 'diane', '/admin/applications')
  await expect(desk.getByRole('heading', { name: 'Applications' })).toBeVisible()
  await expect(desk.getByText('Fall Marriage Retreat · Fall 2028')).toBeVisible()
  await desk.getByRole('textbox', { name: 'Search applications' }).fill('maria.johnson')
  await desk.getByRole('button', { name: 'Review' }).click()

  const reader = desk.getByRole('dialog', { name: 'Maria and David Johnson' })
  await expect(reader.getByText('Ten years in, we want a weekend to reset together.')).toBeVisible()
  await expect(reader.getByText('No charge is captured until the application is approved.')).toBeVisible()
  await reader.getByRole('button', { name: 'Approve application' }).click()
  await desk.getByRole('button', { name: 'Approve and charge $900' }).click()
  await expect(desk.getByText('Approved Maria and David Johnson. $900 charged; spot confirmed.')).toBeVisible()
  await expect(reader.getByText('Paid', { exact: true })).toBeVisible()
  await expect(reader.getByText('Amount charged')).toBeVisible()
  await staff.close()

  // F7 again: Maria is confirmed and paid.
  await page.reload()
  await expect(page.getByText('Confirmed', { exact: true })).toBeVisible()
  await expect(page.getByText('Paid', { exact: true })).toBeVisible()
  await expect(page.getByText("You're confirmed")).toBeVisible()
  await expect(page.getByText('$900 paid on your card ending 4242.')).toBeVisible()
})

test('staff without the CET role can read the queue but not decide @phone', async ({ page }) => {
  await signInAs(page, 'marcus', '/admin/applications')
  await expect(page.getByRole('heading', { name: 'Applications' })).toBeVisible()
  await page.getByRole('tab', { name: /Needs review/ }).click()
  await page.getByRole('button', { name: 'Review' }).first().click()
  await expect(page.getByText('Amount authorized')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Approve application' })).toHaveCount(0)
})

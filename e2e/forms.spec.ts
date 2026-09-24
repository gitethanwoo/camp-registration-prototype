import { expect, signInAs, test } from './fixtures'

// Slice 9 demo on fresh seed data, in this slice's own program (Rome Day Camp). Alex approves the
// v2 form Jamie Dalton wrote and can't approve his own v3; Maria registers Avery and Mia against v2,
// where "Yes" to medication asks for details; Diane reads Avery's answers without the health ones
// (she has no health-data access), and Alex, who has it, sees the medication answers.
// Tests run in order and each leaves data the next one uses.

test.describe.configure({ mode: 'serial' })

let confirmation = ''

test('Alex approves v2 of the Rome form, then sends v3 for approval but cannot approve it himself', async ({
  page,
}) => {
  await signInAs(page, 'alex', '/admin')
  await page.getByRole('link', { name: 'Registration forms' }).click()
  await expect(page.getByRole('heading', { name: 'Registration forms' })).toBeVisible()
  await page.getByRole('combobox', { name: 'Program' }).click()
  await page.getByRole('option', { name: 'Rome Day Camp' }).click()

  await expect(page.getByText('v2 is waiting for approval')).toBeVisible()
  await expect(page.getByText(/Sent by Jamie Dalton/).first()).toBeVisible()

  // The preview answers like the wizard: "Yes" to medication reveals the follow-up.
  const preview = page.getByRole('region', { name: 'Phone preview' })
  await expect(preview.getByText('Medication name and schedule')).toBeHidden()
  await preview
    .getByRole('radiogroup', { name: /medication at camp/ })
    .getByRole('radio', { name: 'Yes' })
    .click()
  await expect(preview.getByText('Medication name and schedule')).toBeVisible()

  await page.getByRole('button', { name: 'Approve and publish' }).click()
  await page.getByRole('alertdialog').getByRole('button', { name: 'Approve and publish' }).click()
  await expect(page.getByText('v2 is live.')).toBeVisible()
  await expect(page.getByText('v2 is waiting for approval')).toBeHidden()

  await page.getByRole('tab', { name: 'Version history' }).click()
  await expect(page.getByText(/approved by Alex Morgan \(ADMIN\)/)).toBeVisible()
  await page.getByRole('tab', { name: 'Questions' }).click()

  // v3: a pickup question for the family, saved as Alex types.
  await page.getByRole('button', { name: 'Start v3' }).click()
  await expect(page.getByText('v3 questions')).toBeVisible()
  await page.getByRole('button', { name: 'Add question' }).click()
  await page.getByLabel('Question', { exact: true }).fill('Who will pick your camper up on Friday?')
  await page.getByRole('radio', { name: /Once per family/ }).click()
  await page.getByRole('checkbox', { name: 'Required' }).click()
  await expect(page.getByRole('button', { name: /Who will pick your camper up on Friday\?/ })).toBeVisible()
  await expect(
    page.getByRole('region', { name: 'Phone preview' }).getByText('Who will pick your camper up on Friday?'),
  ).toBeVisible()
  await page.getByLabel('What changed').fill('Adds a Friday pickup question.')
  await expect(page.getByRole('status').filter({ hasText: 'All changes saved' })).toBeVisible()

  await page.getByRole('button', { name: 'Send for approval' }).click()
  await expect(page.getByText('v3 is waiting for approval')).toBeVisible()
  await expect(page.getByText('You worked on this version, so a different admin has to approve it.')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Approve and publish' })).toBeHidden()
})

test('Maria registers Avery and Mia for Rome Day Camp and answers the medication follow-up', async ({ page }) => {
  await signInAs(page, 'maria', '/programs/day-camp-rome')
  await page.getByRole('button', { name: 'Register', exact: true }).click()
  await expect(page).toHaveURL(/\/register\/\d+/)
  for (const kid of ['Avery', 'Mia']) await page.getByRole('checkbox', { name: `Register ${kid}` }).click()
  await page.getByRole('button', { name: 'Continue' }).click()

  const avery = page.locator('[data-slot="card"]').filter({ hasText: 'About Avery' })
  const mia = page.locator('[data-slot="card"]').filter({ hasText: 'About Mia' })
  const family = page.locator('[data-slot="card"]').filter({ hasText: 'About your family' })

  await avery.getByRole('combobox', { name: /T-shirt size/ }).click()
  await page.getByRole('option', { name: 'Youth L' }).click()
  await avery.getByRole('combobox', { name: /Swimming ability/ }).click()
  await page.getByRole('option', { name: 'Confident swimmer' }).click()
  await expect(avery.getByLabel(/Medication name and schedule/)).toBeHidden()
  await avery.getByRole('radio', { name: 'Yes' }).click()
  await expect(avery.getByLabel(/Medication name and schedule/)).toBeVisible()
  await avery.getByRole('checkbox', { name: 'Sports' }).click()
  await avery.getByRole('checkbox', { name: 'Crafts' }).click()

  await mia.getByRole('combobox', { name: /T-shirt size/ }).click()
  await page.getByRole('option', { name: 'Youth S' }).click()
  await mia.getByRole('combobox', { name: /Swimming ability/ }).click()
  await page.getByRole('option', { name: 'Beginner' }).click()
  await mia.getByRole('radio', { name: 'No' }).click()

  await family.getByRole('radio', { name: 'No' }).click()
  await family.getByLabel(/How many adults/).fill('2')

  // The follow-up is required while it shows.
  await page.getByRole('button', { name: 'Continue' }).click()
  await expect(page.getByText('Avery: 1 required question unanswered.')).toBeVisible()
  await avery.getByLabel(/Medication name and schedule/).fill('Albuterol inhaler, 2 puffs before swimming')
  await page.getByRole('button', { name: 'Continue' }).click()

  const physicians = page.getByLabel(/Physician name/)
  await expect(physicians).toHaveCount(2)
  for (let i = 0; i < 2; i++) await physicians.nth(i).fill('Dr. Adams')
  await page.getByRole('button', { name: 'Continue' }).click()

  const agree = page.getByRole('checkbox', { name: /I agree/ })
  await expect(agree.first()).toBeVisible()
  for (const box of await agree.all()) await box.click()
  const signer = page.getByLabel('Full name')
  if (!(await signer.inputValue())) await signer.fill('Maria Johnson')
  await page.getByRole('button', { name: 'Continue' }).click()

  await page.getByRole('radio', { name: 'Pay in full' }).click()
  await page.getByRole('button', { name: 'Continue to payment' }).click()
  await page.getByRole('button', { name: '4242 4242 4242 4242' }).click()
  await page.getByRole('button', { name: /^Pay \$/ }).click()
  await expect(page.getByRole('heading', { name: 'You’re registered!' })).toBeVisible()
  confirmation = page.url().split('/confirmation/')[1] ?? ''
  expect(confirmation).not.toBe('')

  // F5: the answers, labelled with the version they were given on.
  await page.goto(`/family/registrations/${confirmation}`)
  await expect(page.getByRole('heading', { name: 'Your answers' })).toBeVisible()
  await expect(page.getByText('Answered on registration form v2.')).toBeVisible()
  const averyAnswers = page.getByRole('region', { name: 'Avery answers' })
  await expect(averyAnswers.getByText('Albuterol inhaler, 2 puffs before swimming')).toBeVisible()
  await expect(averyAnswers.getByText('Crafts, Sports')).toBeVisible()
  await expect(page.getByRole('region', { name: 'Mia answers' }).getByText('Medication name and schedule')).toBeHidden()
  await expect(page.getByRole('region', { name: 'Your family answers' }).getByText('2', { exact: true })).toBeVisible()
})

test('Diane reads Avery’s Rome answers without the medication ones; Alex, with health access, sees them', async ({
  page,
  browser,
}) => {
  expect(confirmation).not.toBe('')
  await signInAs(page, 'diane', '/admin/registrations?all=1')
  await page.getByRole('textbox', { name: 'Search registrations' }).fill(confirmation)
  await page.getByRole('cell', { name: /Avery/ }).first().click()
  await expect(page).toHaveURL(/\/admin\/registrations\/\d+/)
  const registration = page.url()

  await expect(page.getByText('Answered on registration form v2.')).toBeVisible()
  const camper = page.getByRole('region', { name: 'Camper answers' })
  await expect(camper.getByText('Confident swimmer')).toBeVisible()
  await expect(
    page.getByRole('region', { name: 'Family answers' }).getByText('Does your family attend a church?'),
  ).toBeVisible()
  // Diane (Customer Experience) has completion status only: the medication answers are withheld.
  await expect(page.getByTestId('health-withheld')).toContainText('Health answers (medication) are hidden.')
  await expect(camper.getByText('Does your camper need medication at camp?')).toHaveCount(0)
  await expect(page.getByText('Albuterol inhaler, 2 puffs before swimming')).toHaveCount(0)

  // Alex has health-data access and Rome opens health details to administrators.
  const admin = await browser.newContext()
  const alex = await admin.newPage()
  await signInAs(alex, 'alex', new URL(registration).pathname)
  await expect(alex.getByText('Answered on registration form v2.')).toBeVisible()
  const adminCamper = alex.getByRole('region', { name: 'Camper answers' })
  await expect(adminCamper.getByText('Does your camper need medication at camp?')).toBeVisible()
  await expect(adminCamper.getByText('Albuterol inhaler, 2 puffs before swimming')).toBeVisible()
  await expect(alex.getByTestId('health-withheld')).toHaveCount(0)
  await admin.close()
})

import type { Page } from '@playwright/test'
import { expect, signInAs, test } from './fixtures'

// Slice 4 demo on fresh seed data: CET front desk search and household 360, discount approvals with
// the Finance threshold, a duplicate merge with an explicit conflict choice, and a family transfer
// request that staff approve while a request into a full pool stays blocked.
// Each demo step changes data for good (a code approved, accounts merged, a camper moved). On a rerun
// against the same database, a step whose work is already done checks the finished state instead.
// A valid answer for any required question in the checkout setup.
const answer = (q: { type: string; options: string[] }) => q.options[0] ?? (q.type === 'YesNo' ? 'No' : 'n/a')

test.describe.configure({ mode: 'serial' })

const getJson = async <T>(page: Page, url: string): Promise<T> => (await page.request.get(url)).json() as Promise<T>

test('Diane searches for the Johnsons, opens the household and adds a note', async ({ page }) => {
  await signInAs(page, 'diane', '/admin')
  await page.getByRole('link', { name: 'Search families' }).first().click()
  await expect(page.getByRole('heading', { name: 'Search families' })).toBeVisible()
  await page.getByPlaceholder('Name, email, phone, or confirmation code').fill('Johnson')
  await expect(page).toHaveURL(/q=Johnson/)
  await page
    .getByRole('cell', { name: /Johnson/ })
    .first()
    .click()

  await expect(page.getByRole('heading', { name: 'Johnson household' })).toBeVisible()
  await expect(page.getByText('Avery').first()).toBeVisible()
  await expect(page.getByText('Health & medical')).toBeVisible()

  const note = `Called about moving Avery to the second June week (${Date.now()}).`
  await page.getByLabel('New note').fill(note)
  await page.getByRole('button', { name: 'Add note' }).click()
  await expect(page.getByText(note, { exact: true })).toBeVisible()
  await expect(page.getByRole('cell', { name: `Note added: ${note}` })).toBeVisible()
  await expect(page.getByText('Diane Carter (CET)').first()).toBeVisible()
})

test('CET cannot approve an over-threshold code; Finance approves it with a note', async ({ page }) => {
  await signInAs(page, 'diane', '/admin/discounts')
  await expect(page.getByRole('heading', { name: 'Discount approval queue' })).toBeVisible()
  const all = await getJson<{ rows: { code: string; decision: string }[] }>(page, '/api/admin/discounts?status=all')
  if (all.rows.find((r) => r.code === 'SUMMERFUN')?.decision === 'Approved') {
    await page.getByRole('tab', { name: /Approved/ }).click()
    await page.getByRole('cell', { name: 'SUMMERFUN' }).click()
    await expect(page.getByText(/Approved by Marcus Lee/)).toBeVisible()
    return
  }
  await page.getByRole('cell', { name: 'SUMMERFUN' }).click()
  await expect(page.getByText('Finance approves this one')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Approve' })).toBeDisabled()
  await page.keyboard.press('Escape')

  await page.context().clearCookies()
  await signInAs(page, 'marcus', '/admin/discounts')
  await page.getByRole('cell', { name: 'SUMMERFUN' }).click()
  await expect(page.getByText('Finance approves this one')).toBeHidden()
  await page.getByRole('button', { name: 'Approve' }).click()
  await expect(page.getByRole('alert').filter({ hasText: 'over the threshold' })).toBeVisible()
  await page.getByLabel(/^Note/).fill('Grace Community partnership, approved against the outreach budget.')
  await page.getByRole('button', { name: 'Approve' }).click()
  await page.getByRole('alertdialog').getByRole('button', { name: 'Approve' }).click()
  await expect(page.getByText('SUMMERFUN approved. It works at checkout now.')).toBeVisible()

  await page.getByRole('tab', { name: /Approved/ }).click()
  await expect(page.getByRole('cell', { name: 'SUMMERFUN' })).toBeVisible()
})

test('Diane merges the duplicate Lee accounts only after resolving the Jordan conflict', async ({ page }) => {
  await signInAs(page, 'diane', '/admin/duplicates')
  await expect(page.getByRole('heading', { name: 'Duplicate accounts' })).toBeVisible()
  if (!(await getJson<unknown[]>(page, '/api/admin/duplicates')).length) {
    await expect(page.getByText('No likely duplicates right now.')).toBeVisible()
    return
  }
  await page.getByRole('row').filter({ hasText: 'Jordan Lee' }).click()

  await expect(page.getByRole('heading', { name: 'Merge duplicate accounts' })).toBeVisible()
  await page.getByRole('button', { name: 'Review and confirm merge' }).click()
  await expect(page.getByRole('alert').filter({ hasText: /Choose how to resolve Jordan Lee/ })).toBeVisible()

  await page.getByRole('radio', { name: /Keep Account A's registration/ }).click()
  await page.getByRole('button', { name: 'Review and confirm merge' }).click()
  const dialog = page.getByRole('alertdialog')
  await expect(dialog.getByText(/Keep Account A's registration/)).toBeVisible()
  await dialog.getByRole('button', { name: 'Yes, merge accounts' }).click()

  await expect(page.getByText('Accounts merged. Salesforce will merge the matching records.')).toBeVisible()
  await expect(page.getByRole('heading', { name: /Lee household/ })).toBeVisible()
  await page.goto('/admin/duplicates')
  await expect(page.getByText('No likely duplicates right now.')).toBeVisible()
})

test('Maria registers Avery for June 12–16, then asks to move to June 19–23', async ({ page }) => {
  await signInAs(page, 'maria', '/family/transfers')
  await expect(page.getByRole('heading', { name: 'Session transfers' })).toBeVisible()
  // Already asked on an earlier run (and maybe already moved): check the request is listed and stop,
  // so a rerun never registers Avery for week 1 a second time.
  const mine = await getJson<{ requests: { participant: string }[] }>(page, '/api/transfers')
  if (mine.requests.some((r) => r.participant.startsWith('Avery'))) {
    await expect(page.getByText(/Avery Johnson: June 12–16, 2028 → June 19–23, 2028/)).toBeVisible()
    return
  }

  // Setup through the real checkout API: the registration wizard belongs to another slice.
  const programs = (await (await page.request.get('/api/programs')).json()) as {
    slug: string
    sessions: { id: number; name: string }[]
  }[]
  const dayCamp = programs.find((p) => p.slug === 'day-camp-atlanta')
  const weekOne = dayCamp?.sessions.find((s) => s.name === 'June week')
  expect(weekOne).toBeTruthy()
  const ctx = await (await page.request.get(`/api/sessions/${weekOne?.id}/register-context`)).json()
  const avery = ctx.participants.find((p: { firstName: string }) => p.firstName === 'Avery')
  if (avery.status !== 'registered') {
    const questions = ctx.questions as { key: string; type: string; scope: string; options: string[] }[]
    const token = (
      await (
        await page.request.post('/api/fiserv-sandbox/tokenize', { data: { cardNumber: '4242424242424242' } })
      ).json()
    ).token
    const res = await page.request.post('/api/checkout', {
      data: {
        idempotencyKey: `e2e-staffcx-${Date.now()}`,
        sessionId: weekOne?.id,
        participants: [
          {
            personId: avery.id,
            answers: Object.fromEntries(
              questions.filter((q) => q.scope === 'Participant').map((q) => [q.key, answer(q)]),
            ),
            health: { physicianName: 'Dr. Patel', physicianPhone: '(404) 555-0140' },
          },
        ],
        householdAnswers: Object.fromEntries(
          questions.filter((q) => q.scope === 'Household').map((q) => [q.key, answer(q)]),
        ),
        waivers: ctx.waivers.map((w: { id: number; perParticipant: boolean }) => ({
          waiverId: w.id,
          personId: w.perParticipant ? avery.id : null,
          signerName: 'Maria Johnson',
        })),
        paymentOption: 'Full',
        discountCode: null,
        cardToken: token,
      },
    })
    expect(res.status(), await res.text()).toBe(200)
  }

  await page.goto('/family/transfers')
  await page.getByRole('link', { name: 'Request transfer' }).first().click()

  await expect(page.getByRole('heading', { name: 'Request a session transfer' })).toBeVisible()
  await expect(page.getByText('This is a request, not an immediate transfer')).toBeVisible()
  await page.getByRole('combobox', { name: 'Select new session' }).click()
  await page.getByRole('option', { name: /June 19–23/ }).click()
  await expect(page.getByText(/spots? left/).last()).toBeVisible()
  await page.getByRole('button', { name: 'Submit transfer request' }).click()
  await expect(page.getByRole('alert').filter({ hasText: /Tell us why/ })).toBeVisible()
  await page.getByLabel('Reason for transfer request').fill("We'll be at a family reunion the first week of June.")
  await page.getByRole('button', { name: 'Submit transfer request' }).click()
  await expect(page.getByText('Request sent')).toBeVisible()
  await expect(page.getByText(/Avery stays in June 12–16, 2028 until they approve/)).toBeVisible()
})

test('Diane sees the full-pool request blocked and approves Avery’s move', async ({ page }) => {
  await signInAs(page, 'diane', '/admin/transfers')
  await expect(page.getByRole('heading', { name: 'Transfer requests' })).toBeVisible()

  await page.getByRole('row').filter({ hasText: '· blocked' }).first().click()
  await expect(page.getByText(/pool is full \(20 of 20\)/).first()).toBeVisible()
  await expect(page.getByRole('button', { name: 'Approve transfer' })).toBeDisabled()
  await page.keyboard.press('Escape')

  const pending = await getJson<{ rows: { participant: string }[] }>(page, '/api/admin/transfers?status=pending')
  if (!pending.rows.some((r) => r.participant === 'Avery Johnson')) {
    await page.getByRole('tab', { name: 'Approved' }).click()
    await expect(page.getByRole('row').filter({ hasText: 'Avery Johnson' })).toBeVisible()
    return
  }
  await page.getByRole('row').filter({ hasText: 'Avery Johnson' }).click()
  await expect(page.getByRole('dialog').getByText('Avery Johnson').first()).toBeVisible()
  await page.getByRole('button', { name: 'Approve transfer' }).click()
  await expect(page.getByText(/Avery Johnson moved to June 19–23/)).toBeVisible()

  await page.getByRole('tab', { name: 'Approved' }).click()
  await expect(page.getByRole('row').filter({ hasText: 'Avery Johnson' })).toBeVisible()
})

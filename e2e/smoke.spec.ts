import { expect, signInAs, test } from './fixtures'

test('a returning family signs in and sees their household', async ({ page }) => {
  await signInAs(page, 'maria', '/family')
  await expect(page).toHaveURL(/\/family$/)
  await expect(page.getByText('Avery').first()).toBeVisible()
  await expect(page.getByRole('button', { name: /Maria Johnson/ })).toBeVisible()
})

test('staff sign in to the console and the sidebar lists their pages', async ({ page }) => {
  await signInAs(page, 'diane', '/admin')
  await expect(page.getByRole('link', { name: 'Registrations' })).toBeVisible()
  await expect(page.getByText('Diane Carter')).toBeVisible()
})

test('a family account cannot open the staff console', async ({ page }) => {
  await signInAs(page, 'maria', '/admin')
  await expect(page.getByRole('heading', { name: 'Staff only' })).toBeVisible()
})

test('browsing programs needs no sign-in @phone', async ({ page }) => {
  await page.goto('/programs')
  await expect(page.getByRole('link', { name: /Day Camp/ }).first()).toBeVisible()
  await expect(page.getByRole('button', { name: 'Sign in' })).toBeVisible()
})

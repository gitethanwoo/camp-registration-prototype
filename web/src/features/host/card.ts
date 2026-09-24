import { api } from '@/lib/api'

export interface CardInput {
  number: string
  expiry: string
  cvc: string
  zip: string
}

export const emptyCard = (): CardInput => ({
  number: '',
  expiry: '',
  cvc: '',
  zip: '',
})

export const cardComplete = (c: CardInput) =>
  c.number.replace(/\D/g, '').length >= 15 &&
  /^\d{2}\s*\/\s*\d{2}$/.test(c.expiry) &&
  c.cvc.length >= 3 &&
  c.zip.length >= 5

// Slice copy of features/admittance/card.ts (slices do not import each other).
/** In production the token comes from Fiserv's hosted fields; the card number never reaches our API. */
export async function tokenize(c: CardInput) {
  return (
    await api.post<{ token: string }>('/fiserv-sandbox/tokenize', {
      cardNumber: c.number,
    })
  ).token
}

export const newKey = () => crypto.randomUUID()

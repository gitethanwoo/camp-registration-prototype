import { reactive, watch } from 'vue'
import type { HealthForm, PaymentOption } from '@/lib/types'

export interface RegistrationDraft {
  step: number
  selected: number[]
  answers: Record<number, Record<string, string>>
  householdAnswers: Record<string, string>
  health: Record<number, HealthForm>
  agreed: Record<string, boolean>
  signer: string
  paymentOption: PaymentOption
  discountCode: string
  idempotencyKey: string
}

const newKey = () => crypto.randomUUID()

function fresh(): RegistrationDraft {
  return {
    step: 0,
    selected: [],
    answers: {},
    householdAnswers: {},
    health: {},
    agreed: {},
    signer: '',
    paymentOption: 'Deposit',
    discountCode: '',
    idempotencyKey: newKey(),
  }
}

/**
 * Wizard state for one session, kept in sessionStorage so a refresh or back-navigation
 * doesn't lose a parent's answers. Nothing is held server-side until checkout; seats
 * are claimed atomically at payment time, not while filling out forms.
 */
export function useRegistrationDraft(sessionId: number) {
  const key = `draft.session.${sessionId}`
  let initial = fresh()
  try {
    const raw = sessionStorage.getItem(key)
    if (raw) initial = { ...initial, ...JSON.parse(raw) }
  } catch {
    /* storage unavailable: draft lives in memory only */
  }

  const draft = reactive<RegistrationDraft>(initial)
  watch(
    draft,
    (d) => {
      try {
        sessionStorage.setItem(key, JSON.stringify(d))
      } catch {
        /* ignore */
      }
    },
    { deep: true },
  )

  return {
    draft,
    /** After a decline the next attempt is a new payment intent. */
    rotateKey: () => {
      draft.idempotencyKey = newKey()
    },
    clear: () => {
      Object.assign(draft, fresh())
      try {
        sessionStorage.removeItem(key)
      } catch {
        /* ignore */
      }
    },
  }
}

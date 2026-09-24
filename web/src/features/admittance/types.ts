/** Shapes returned by /api/admittance and /api/admin/admittance. Money is in cents. */

export type Stage = 'Draft' | 'Submitted' | 'UnderReview' | 'InfoRequested' | 'Approved' | 'Declined' | 'Waitlisted'
export type PaymentState = 'None' | 'Authorized' | 'Expiring' | 'Expired' | 'CardNeeded' | 'Paid' | 'Voided'

export interface SessionInfo {
  id: number
  name: string
  startDate: string
  endDate: string
  priceCents: number
  program: { name: string; slug: string; location: string; ministry: string; ministryCode: string }
}

export interface Payment {
  state: PaymentState
  amountCents: number
  cardLast4: string | null
  authorizedAt: string | null
  expiresAt: string | null
  canUpdateCard: boolean
}

export interface FormQuestion {
  key: string
  section: string
  label: string
  help: string | null
  type: 'Select' | 'LongText' | 'Text' | 'YesNo'
  required: boolean
  options: string[]
  maxLength: number
  showWhenKey: string | null
  showWhenValue: string | null
}

export interface FormSection {
  key: string
  title: string
  description: string
}

export interface Adult {
  id: number
  firstName: string
  lastName: string
  email: string | null
}

export interface ApplyContext {
  session: SessionInfo
  seatsLeft: number
  applicant: Adult | null
  otherAdults: Adult[]
  sections: FormSection[]
  questions: FormQuestion[]
  application: {
    id: number
    stage: Stage
    currentStep: number
    spousePersonId: number | null
    spouseFirstName: string
    spouseLastName: string
    spouseEmail: string | null
    answers: Record<string, string>
    updatedAt: string
  } | null
}

export interface FamilyApplication {
  id: number
  stage: Stage
  status: string
  couple: string
  session: SessionInfo
  currentStep: number
  submittedAt: string | null
  reviewStartedAt: string | null
  decidedAt: string | null
  decisionNote: string | null
  infoRequest: string | null
  infoRequestedAt: string | null
  infoResponse: string | null
  infoRespondedAt: string | null
  payment: Payment
  confirmationCode: string | null
}

export interface FamilyListItem {
  id: number
  stage: Stage
  status: string
  couple: string
  session: SessionInfo
  submittedAt: string | null
  paymentState: PaymentState
}

export interface StaffSession {
  session: SessionInfo
  capacity: number
  remaining: number
  pending: number
}

export interface QueueRow {
  id: number
  couple: string
  email: string
  stage: Stage
  paymentState: PaymentState
  amountCents: number
  expiresAt: string | null
  submittedAt: string | null
  lastActivity: string
}

export interface Queue {
  session: SessionInfo
  capacity: number
  reserved: number
  remaining: number
  counts: {
    all: number
    submitted: number
    underReview: number
    approved: number
    waitlisted: number
    declined: number
  }
  rows: QueueRow[]
}

export interface StaffDetail {
  id: number
  stage: Stage
  status: string
  couple: string
  session: SessionInfo
  sessionRemaining: number
  applicant: { firstName: string; lastName: string; email: string }
  spouse: { firstName: string; lastName: string; email: string | null }
  household: { phone: string | null; city: string | null }
  submittedAt: string | null
  reviewStartedAt: string | null
  decidedAt: string | null
  reviewedBy: string | null
  decisionNote: string | null
  infoRequest: string | null
  infoRequestedAt: string | null
  infoResponse: string | null
  infoRespondedAt: string | null
  registrationId: number | null
  lastActivity: string
  answers: { key: string; label: string; section: string; answer: string }[]
  payment: Payment
  history: { actor: string; action: string; detail: string; createdAt: string }[]
}

/** Staff-facing stage words. InfoRequested reads as a sub-state of review. */
export const stageLabels: Record<Stage, string> = {
  Draft: 'Draft',
  Submitted: 'Submitted',
  UnderReview: 'Under review',
  InfoRequested: 'Info requested',
  Approved: 'Approved',
  Declined: 'Declined',
  Waitlisted: 'Waitlisted',
}

export const isPending = (s: Stage) => s === 'Submitted' || s === 'UnderReview' || s === 'InfoRequested'

/** Family-facing registration status words (global vocabulary). */
export const statusLabels: Record<string, string> = {
  Draft: 'Draft',
  ApplicationPending: 'Application pending',
  PaymentPending: 'Payment pending',
  Confirmed: 'Confirmed',
  Waitlisted: 'Waitlisted',
  Declined: 'Declined',
}

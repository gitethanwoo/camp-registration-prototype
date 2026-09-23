export type PoolState = 'open' | 'low' | 'full'

export interface PoolAvailability {
  id: number
  name: string
  capacity: number
  reserved: number
  remaining: number
  waitlisted: number
  state: PoolState
}

export interface ProgramSummary {
  slug: string
  name: string
  tagline: string
  location: string
  imageUrl: string
  hostOrganization: string | null
  ministry: string
  type: 'Standard' | 'Admittance' | 'Cohort'
  sessions: { id: number, name: string, startDate: string, endDate: string, priceCents: number, depositCents: number, gradeMin: number, gradeMax: number, capacity: number, remaining: number }[]
}

export interface ProgramDetail {
  slug: string
  name: string
  tagline: string
  description: string
  location: string
  imageUrl: string
  hostOrganization: string | null
  ministry: string
  type: 'Standard' | 'Admittance' | 'Cohort'
  healthMechanism: 'Embedded' | 'ThirdParty' | 'CampDoc'
  requirements: string[]
  sessions: { id: number, name: string, startDate: string, endDate: string, priceCents: number, depositCents: number, planInstallments: number, pools: PoolAvailability[] }[]
  asOf: string
}

export interface Participant {
  id: number
  firstName: string
  lastName: string
  dateOfBirth: string
  gender: 'Male' | 'Female'
  grade: number
  gradeLabel: string
  status: 'eligible' | 'ineligible' | 'registered' | 'waitlisted'
  reason: string | null
  pool: PoolAvailability | null
  basicHealth: { dietary: string | null, allergies: string | null, adaNeeds: string | null }
}

export interface Question {
  key: string
  label: string
  type: 'Select' | 'Text' | 'YesNo'
  scope: 'Participant' | 'Household'
  required: boolean
  options: string[]
  showWhenKey: string | null
  showWhenValue: string | null
}

export interface Waiver {
  id: number
  title: string
  version: number
  effectiveDate: string
  body: string
  perParticipant: boolean
}

export interface RegisterContext {
  session: { id: number, name: string, startDate: string, endDate: string, priceCents: number, depositCents: number, planInstallments: number, balanceDueDate: string }
  program: { slug: string, name: string, location: string, imageUrl: string, type: string, healthMechanism: 'Embedded' | 'ThirdParty' | 'CampDoc' }
  household: { name: string, email: string, phone: string, city: string, signer: string }
  participants: Participant[]
  questions: Question[]
  waivers: Waiver[]
}

export type PaymentOption = 'Deposit' | 'Full' | 'Plan'

export interface Quote {
  lines: { personId: number, name: string, priceCents: number, discountCents: number }[]
  subtotalCents: number
  discountCents: number
  totalCents: number
  dueTodayCents: number
  remainingCents: number
  paymentOption: PaymentOption
  schedule: { dueDate: string | null, amountCents: number, label: string }[]
  appliedDiscountCode: string | null
  discountError: string | null
}

export interface HealthForm {
  dietary: string
  allergies: string
  adaNeeds: string
  medications: string
  physicianName: string
  physicianPhone: string
  insuranceProvider: string
}

export interface ChecklistItem {
  key: string
  participant: string
  context: string
  title: string
  done: boolean
  action: string
  kind: 'waiver' | 'health' | 'campdoc' | 'balance'
}

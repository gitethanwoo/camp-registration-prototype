/** Shapes returned by /api/family/* (Features/Family on the API). Money is always in cents. */

export type PaymentStatus =
  | 'Paid'
  | 'Deposit paid'
  | 'Balance due'
  | 'Plan active'
  | 'Installment failed'
  | 'Refunded'
  | ''

export interface ChecklistEntry {
  key: string
  participant: string
  context: string
  title: string
  detail: string
  done: boolean
  action: string
  kind: 'waiver' | 'health' | 'campdoc' | 'balance'
  href: string
}

export interface Money {
  totalCents: number
  paidCents: number
  balanceCents: number
  paymentStatus: PaymentStatus
}

export interface Overview {
  name: string
  seasonYear: number
  members: {
    id: number
    firstName: string
    lastName: string
    isAdult: boolean
    role: string | null
    gradeLabel: string | null
  }[]
  registrations: (Money & {
    confirmationCode: string
    program: string
    session: string
    startDate: string
    endDate: string
    participants: string
    count: number
    plan: { installments: number; eachCents: number; nextDueDate: string | null; nextAmountCents: number | null } | null
  })[]
  waitlist: {
    id: number
    participant: string
    program: string
    session: string
    startDate: string
    endDate: string
    pool: string
    position: number
    status: 'Waiting' | 'Offered'
    offerExpiresAt: string | null
  }[]
  checklist: ChecklistEntry[]
}

export type Gender = 'Male' | 'Female'

export interface MemberProfile {
  id: number
  firstName: string
  lastName: string
  dateOfBirth: string | null
  gender: Gender
  isAdult: boolean
  role: string | null
  email: string | null
  dietary: string | null
  allergies: string | null
  adaNeeds: string | null
  seasonYear: number
  age: number | null
  gradeLabel: string | null
  canClaimOwnAccount: boolean
  registeredFor: string[]
}

export interface MemberRequest {
  firstName: string
  lastName: string
  dateOfBirth: string | null
  gender: Gender | null
  isAdult: boolean
  email: string | null
  dietary: string | null
  allergies: string | null
  adaNeeds: string | null
}

export interface HouseholdAccess {
  name: string
  seasonYear: number
  canManage: boolean
  adults: {
    id: number
    firstName: string
    lastName: string
    email: string | null
    role: string | null
    isYou: boolean
    access: 'Primary owner' | 'Co-owner' | 'No account access'
    permissions: string[]
  }[]
  invitations: {
    id: number
    firstName: string
    lastName: string
    email: string
    invitedBy: string
    createdAt: string
    expiresAt: string
    expired: boolean
  }[]
  dependents: { id: number; firstName: string; lastName: string; gradeLabel: string }[]
}

export interface RegistrationCard extends Money {
  group: 'upcoming' | 'past' | 'cancelled'
  confirmationCode: string
  ministry: string
  program: string
  session: string
  startDate: string
  endDate: string
  status: string
  participants: { id: number; name: string; gradeLabel: string | null; status: string; position: number | null }[]
  planInstallments: number
  planEachCents: number | null
}

export interface RegistrationList {
  upcoming: RegistrationCard[]
  past: RegistrationCard[]
  cancelled: RegistrationCard[]
}

export interface RegistrationDetail {
  confirmationCode: string
  program: {
    name: string
    slug: string
    location: string
    ministry: string
    healthMechanism: string
    isPublished: boolean
  }
  session: { id: number; name: string; startDate: string; endDate: string }
  isPast: boolean
  signer: string
  participants: {
    registrationId: number
    personId: number
    firstName: string
    lastName: string
    gradeLabel: string | null
    pool: string
    status: string
    healthStatus: string
    waivers: {
      id: number
      title: string
      version: number
      perParticipant: boolean
      signed: boolean
      signedBy: string | null
    }[]
  }[]
  waivers: {
    id: number
    title: string
    version: number
    effectiveDate: string
    body: string
    perParticipant: boolean
  }[]
  checklist: ChecklistEntry[]
  payment: Money & {
    option: 'Deposit' | 'Full' | 'Plan'
    priceCents: number
    activeCount: number
    discountCents: number
    discountCode: string | null
    balanceDueDate: string
    installments: Installment[]
  }
  household: { email: string }
}

export interface Installment {
  sequence: number
  dueDate: string
  amountCents: number
  status: 'Scheduled' | 'Paid' | 'Failed'
  graceUntil?: string | null
  /** Set when a balance payment settled this installment early; it was never charged on its own. */
  coveredOn?: string | null
}

export interface HistoryEntry {
  id: number
  kind: 'Charge' | 'Refund'
  label: string
  amountCents: number
  cardLast4: string | null
  processorRef: string | null
  createdAt: string
  receiptNumber: string
}

export interface Payments extends Money {
  confirmationCode: string
  program: string
  session: { name: string; startDate: string; endDate: string; balanceDueDate: string }
  household: { name: string; email: string; payer: string }
  lines: { name: string; gradeLabel: string | null; priceCents: number; discountCents: number; cancelled: boolean }[]
  activeCount: number
  discountCode: string | null
  discountCents: number
  option: 'Deposit' | 'Full' | 'Plan'
  history: HistoryEntry[]
  installments: Installment[]
  failedInstallment: { sequence: number; dueDate: string; amountCents: number; graceUntil: string } | null
  nextCharge: { dueDate: string; amountCents: number } | null
}

export interface PayResult {
  outcome: 'Succeeded' | 'Declined' | 'NotFound' | 'NothingOwed' | 'AlreadyProcessing' | 'Invalid'
  amountCents: number
  message: string | null
  cardLast4: string | null
}

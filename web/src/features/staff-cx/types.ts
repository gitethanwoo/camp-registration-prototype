// Response shapes for the staff-cx endpoints (api/Camp.Api/Features/StaffCx).

export interface SearchRow {
  id: number
  name: string
  email: string
  phone: string
  city: string
  members: { name: string; isAdult: boolean }[]
  ministries: string[]
  recentActivity: string | null
  recentActivityAt: string | null
}
export interface SearchResult {
  total: number
  rows: SearchRow[]
}

export interface HouseholdDetail {
  id: number
  name: string
  email: string
  phone: string
  city: string
  mergedIntoHouseholdId: number | null
  mergedFrom: { mergedHouseholdId: number; actor: string; createdAt: string }[]
  salesforce: { id: string | null; status: 'Synced' | 'Pending' | 'Not linked'; lastSyncAt: string | null }
  adults: { id: number; name: string; role: string; email: string | null }[]
  children: { id: number; name: string; dateOfBirth: string; grade: string; gradeYear: number }[]
  registrations: {
    id: number
    participant: string
    program: string
    ministry: string
    session: string
    startDate: string
    endDate: string
    pool: string
    status: string
    priceCents: number
    discountCents: number
    paidCents: number
    balanceCents: number
    payment: string
    confirmationCode: string | null
  }[]
  totals: { priceCents: number; paidCents: number; balanceCents: number }
  waitlist: {
    id: number
    participant: string
    program: string
    session: string
    pool: string
    position: number
    status: string
    offerExpiresAt: string | null
  }[]
  transfers: { id: number; participant: string; from: string; to: string; status: string; createdAt: string }[]
  notes: HouseholdNote[]
  verification: { key: string; label: string; checked: boolean; checkedBy: string | null; checkedAt: string | null }[]
  duplicates: { otherHouseholdId: number; householdA: number; householdB: number; reason: string }[]
  history: { actor: string; action: string; detail: string; createdAt: string }[]
}
export interface HouseholdNote {
  id: number
  body: string
  author: string
  createdAt: string
}

export type Decision = 'Pending' | 'Approved' | 'Rejected'
export interface DiscountRow {
  id: number
  code: string
  kind: 'Percent' | 'Flat'
  value: number
  description: string
  requesterType: 'Host' | 'Partner'
  requestedBy: string
  organization: string
  program: string
  sessionPriceCents: number
  validFrom: string
  validTo: string
  maxUses: number | null
  stackable: boolean
  overridesOtherCodes: boolean
  requesterNote: string | null
  requestedAt: string
  decision: Decision
  reviewedBy: string | null
  reviewNote: string | null
  reviewedAt: string | null
  codeStatus: string
  percentOfPrice: number
  overThreshold: boolean
}
export interface DiscountQueue {
  counts: { pending: number; approved: number; rejected: number; all: number }
  thresholdPercent: number
  canApproveOverThreshold: boolean
  rows: DiscountRow[]
}

export interface DuplicateSummary {
  householdA: number
  householdB: number
  reason: string
  sharedPeople: string[]
  a: { id: number; name: string; email: string; phone: string; members: string[] }
  b: { id: number; name: string; email: string; phone: string; members: string[] }
  conflicts: number
}
export interface Holding {
  kind: 'registration' | 'waitlist'
  id: number
  personId: number
  person: string
  sessionId: number
  session: string
  detail: string
  status: string
  position: number | null
}
export interface DuplicateAccount {
  id: number
  name: string
  email: string
  phone: string
  city: string
  salesforceId: string | null
  members: { id: number; name: string; isAdult: boolean; role: string | null; dateOfBirth: string }[]
  holdings: Holding[]
}
export interface MergeConflict {
  key: string
  person: string
  session: string
  summary: string
  a: string
  b: string
  options: { value: string; label: string; dropsWaitlistEntryId: number }[]
}
export interface DuplicateDetail {
  reason: string
  sharedPeople: string[]
  a: DuplicateAccount
  b: DuplicateAccount
  fields: { key: 'name' | 'email' | 'phone' | 'city'; label: string; a: string; b: string }[]
  conflicts: MergeConflict[]
  salesforce: { a: string | null; b: string | null }
}

export interface Requirement {
  label: string
  state: 'Ok' | 'Review' | 'Blocked'
  detail: string
}
export interface TransferCheck {
  sessionId: number
  session: string
  priceCents: number
  priceDifferenceCents: number
  poolId: number | null
  pool: string | null
  capacity: number | null
  reserved: number | null
  spotsLeft: number | null
  blockers: string[]
  requirements: Requirement[]
  newBalanceCents: number
  refundCents: number
  canMove: boolean
}
export type TransferStatus = 'Pending' | 'Approved' | 'Denied'
export interface StaffTransferRow {
  id: number
  registrationId: number
  householdId: number
  participant: string
  program: string
  fromSession: string
  fromDates: string
  toSession: string
  toDates: string
  reason: string
  requestedBy: string
  createdAt: string
  status: TransferStatus
  decidedBy: string | null
  decidedAt: string | null
  decisionNote: string | null
  priceDifferenceCents: number
  refundCents: number | null
  toPool: string | null
  toCapacity: number | null
  toReserved: number | null
  blocked: boolean
  blockers: string[]
}
export interface StaffTransferDetail {
  request: StaffTransferRow
  from: { pool: string; capacity: number; reserved: number }
  registration: {
    id: number
    status: string
    priceCents: number
    discountCents: number
    paidCents: number
    balanceCents: number
    payment: string
  }
  check: TransferCheck | null
}
export interface FamilyTransferRow {
  id: number
  registrationId: number
  participant: string
  program: string
  from: string
  to: string
  reason: string
  createdAt: string
  status: TransferStatus
  decisionNote: string | null
  decidedAt: string | null
  priceDifferenceCents: number | null
  refundCents: number | null
}

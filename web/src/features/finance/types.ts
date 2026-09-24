// Shapes returned by /api/admin/finance/*, /api/admin/scholarships and /api/family/scholarships. Money is in cents.

// ── FN1 ──
export interface ReportOptions {
  ministries: { id: number; code: string; name: string }[]
  programs: { id: number; ministryId: number; name: string }[]
  sessions: { id: number; programId: number; name: string; startDate: string; endDate: string }[]
}
export interface ReportRow {
  programId: number | null
  sessionId: number | null
  program: string
  session: string
  registrations: number
  attended: number
  settledRevenueCents: number
  contractedCents: number
}
export interface Report {
  from: string
  to: string
  scopeLabel: string
  options: ReportOptions
  registrations: number
  settledRevenue: {
    amountCents: number
    batchGrossCents: number
    unattributedCents: number
    batches: number
    certified: boolean
  }
  attendance: { attended: number; registered: number; sessions: number }
  demographics: {
    total: number
    gender: { label: string; count: number }[]
    grades: { label: string; count: number }[]
  }
  registrationsByPeriod: {
    unit: 'week' | 'month'
    periods: { label: string; start: string; end: string; count: number }[]
  }
  revenueByProgram: { program: string; amountCents: number }[]
  rows: ReportRow[]
}

// ── FN2 ──
export type JournalStatus = 'Pending' | 'Posted' | 'Failed'
export interface SettlementSummary {
  id: number
  reference: string
  settledOn: string
  transactions: number
  grossCents: number
  feeCents: number
  unmatched: number
  journalStatus: JournalStatus | null
}
export interface SettlementList {
  batches: SettlementSummary[]
  unsettled: { count: number; amountCents: number }
}
export type LineStatus = 'Matched' | 'Unmatched' | 'Resolved'
export interface SettlementLine {
  id: number
  kind: 'Payment' | 'Refund' | 'Fee'
  processorRef: string
  transactedAt: string
  amountCents: number
  description: string
  cardholderName: string | null
  cardLast4: string | null
  status: LineStatus
  unmatchedReason: string | null
  resolution: 'MatchedToRegistration' | 'Adjustment' | null
  resolutionNote: string | null
  resolvedBy: string | null
  resolvedAt: string | null
  confirmationCode: string | null
  program: string | null
  session: string | null
}
export interface SettlementDetail {
  id: number
  reference: string
  settledOn: string
  receivedAt: string
  processor: { grossCents: number; feeCents: number; netCents: number }
  platform: { grossCents: number; payments: number }
  transactions: { total: number; matched: number; unmatched: number; resolved: number; unmatchedCents: number }
  journal: { id: number | null; reference: string | null; status: JournalStatus | 'Not created'; detail: string }
  lines: SettlementLine[]
}
export interface Candidate {
  confirmationCode: string
  household: string
  payer: string | null
  campers: string[]
  program: string
  session: string
  startDate: string
  endDate: string
  balanceCents: number
  nameMatches: boolean
  canTake: boolean
}

// ── FN3 ──
export type Stage = 'Retry scheduled' | 'In grace period' | 'Needs attention'
export interface PlanException {
  installmentId: number
  family: string
  email: string
  confirmationCode: string
  program: string
  session: string
  sequence: number
  amountCents: number
  failedOn: string
  attempts: number
  declineReason: string
  nextRetryOn: string | null
  graceEndsOn: string
  daysToPolicyAction: number
  status: 'Installment failed'
  stage: Stage
  lastContactedAt: string | null
}
export interface PlanExceptionList {
  counts: { failed: number; outstandingCents: number; retryScheduled: number; needsAttention: number }
  programs: { id: number; name: string }[]
  rows: PlanException[]
}
export interface PlanExceptionDetail {
  row: PlanException
  contact: { name: string; email: string; phone: string }
  card: { brand: string; last4: string } | null
  plan: {
    depositCents: number
    totalCents: number
    count: number
    installments: {
      id: number
      sequence: number
      dueDate: string
      amountCents: number
      status: 'Scheduled' | 'Paid' | 'Failed'
    }[]
  }
  timeline: { at: string; title: string; actor: string; detail: string }[]
}

// ── FN4 ──
export interface JournalRow {
  id: number
  reference: string
  settledOn: string
  source: string
  settlementReference: string
  entries: number
  debitCents: number
  creditCents: number
  status: JournalStatus
  errorDetail: string | null
}
export interface JournalList {
  counts: { total: number; posted: number; pending: number; failed: number }
  rows: JournalRow[]
  waiting: { id: number; reference: string; settledOn: string; unmatched: number }[]
}
export interface JournalDetail {
  id: number
  reference: string
  createdAt: string
  status: JournalStatus
  errorDetail: string | null
  settlement: { id: number; reference: string; settledOn: string; lines: number; matched: number; resolved: number }
  entries: number
  grossCents: number
  feeCents: number
  netCents: number
  debitCents: number
  creditCents: number
  balanced: boolean
  journalLines: {
    account: string
    accountName: string
    department: string | null
    description: string
    debitCents: number
    creditCents: number
  }[]
  events: { status: JournalStatus; detail: string; actor: string; at: string }[]
}

// ── O6 / O7 ──
export type ScholarshipStatus = 'Submitted' | 'Approved' | 'Denied'
export interface Household {
  name: string
  contact: string
  email: string
  phone: string
  city: string
}
export interface FamilyScholarships {
  household: Household
  incomeBands: string[]
  maxDocumentBytes: number
  orders: {
    confirmationCode: string
    program: string
    session: string
    startDate: string
    endDate: string
    location: string
    balanceCents: number
    canApply: boolean
    campers: {
      registrationId: number
      firstName: string
      name: string
      grade: number
      priceCents: number
      discountCents: number
      balanceCents: number
    }[]
  }[]
  applications: {
    id: number
    status: ScholarshipStatus
    submittedAt: string
    requestedCents: number
    awardCents: number
    decidedAt: string | null
    confirmationCode: string | null
    program: string | null
    session: string | null
    campers: string[]
    documentName: string | null
  }[]
}
export interface ScholarshipRow {
  id: number
  status: ScholarshipStatus
  submittedAt: string
  submittedBy: string
  requestedCents: number
  awardCents: number
  household: string
  program: string
  session: string
  confirmationCode: string
  campers: string[]
  hasDocument: boolean
}
export interface ScholarshipQueue {
  counts: { all: number; submitted: number; approved: number; denied: number; awardedCents: number }
  rows: ScholarshipRow[]
}
export interface ScholarshipDetail {
  id: number
  status: ScholarshipStatus
  submittedAt: string
  submittedBy: string
  requestedCents: number
  incomeBand: string
  reason: string
  awardCents: number
  decidedBy: string | null
  decidedAt: string | null
  decisionNote: string | null
  household: Household
  confirmationCode: string
  program: string
  session: string
  startDate: string
  endDate: string
  campers: {
    registrationId: number
    name: string
    grade: number
    priceCents: number
    discountCents: number
    awardCents: number
    paidCents: number
    balanceCents: number
  }[]
  eligibleCents: number
  owedCents: number
  maxAwardCents: number
  document: { fileName: string; contentType: string; sizeBytes: number; uploadedAt: string } | null
}

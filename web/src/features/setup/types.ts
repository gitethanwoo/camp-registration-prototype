// Shapes returned by /api/admin/setup/* and /api/admin/audit-log. Money is always cents from the server.

export type PublishState = 'Draft' | 'PendingApproval' | 'Published'

export interface ApprovalStep {
  sequence: number
  role: string
  description: string
  approvedBy: string | null
  approvedAt: string | null
  next: boolean
}

export interface ProgramRow {
  id: number
  name: string
  slug: string
  ministry: { id: number; code: string; name: string }
  type: string
  typeLabel: string
  healthMechanism: string
  location: string
  tagline: string
  description: string
  isPublished: boolean
  state: PublishState
  stateLabel: string
  questions: number
  waivers: { id: number; title: string; version: number }[]
  sessions: {
    id: number
    name: string
    startDate: string
    endDate: string
    priceCents: number
    capacity: number
    pools: number
    registered: number
  }[]
  registered: number
  submittedBy: string | null
  submittedAt: string | null
  returnNote: string | null
  steps: ApprovalStep[]
  nextStep: string | null
  canApprove: boolean
  approvalBlock: string | null
}

export interface ProgramList {
  ministries: { id: number; code: string; name: string }[]
  rows: ProgramRow[]
}

export interface SetupPool {
  id: number
  name: string
  gender: 'Male' | 'Female' | null
  gradeMin: number
  gradeMax: number
  capacity: number
  taken: number
  open: number
  waitlisted: number
  removable: boolean
}

export interface SessionSetup {
  id: number
  name: string
  startDate: string
  endDate: string
  location: string
  registrationOpensAt: string | null
  priorityOpensAt: string | null
  waitlistModeLabel: string
  capacityFromOpera: boolean
  registered: number
  program: { id: number; name: string; ministry: string; type: string; state: PublishState; stateLabel: string }
  status: string
  pools: SetupPool[]
  totals: { capacity: number; taken: number; open: number; waitlisted: number }
  fullPools: string[]
  locations: string[]
}

export type RefundBasis = 'AmountPaid' | 'BeyondDeposit'

export interface TierInput {
  daysBefore: number
  refundPercent: number
  basis: RefundBasis
  adminFeeCents: number
}

export interface PricingInput {
  priceCents: number
  depositCents: number
  planInstallments: number
  balanceDueDate: string
  tiers: TierInput[]
}

export interface PricingPreview {
  errors: Record<string, string[]>
  totalCents: number
  depositCents: number
  remainingCents: number
  planOffered: boolean
  schedule: { label: string; dueDate: string; amountCents: number }[]
  scheduleTotalCents: number
  tiers: (TierInput & {
    from: string | null
    to: string
    rule: string
    terms: string
    exampleRefundCents: number
  })[]
}

export interface PricingPage {
  session: { id: number; name: string; startDate: string; endDate: string; program: string; programType: string }
  saved: PricingInput
  registrations: number
  preview: PricingPreview
}

export type DiscountKind = 'Percent' | 'Flat'
export type RuleStatus = 'Active' | 'Pending approval' | 'Inactive'

export interface DiscountRuleRow {
  id: number
  code: string
  name: string
  kind: DiscountKind
  value: number
  description: string
  programId: number | null
  sessionId: number | null
  scope: string
  validFrom: string | null
  validTo: string | null
  maxUses: number | null
  uses: number
  stackable: boolean
  status: RuleStatus
  statusReason: string
  source: string
  editable: boolean
  hasRule: boolean
  active: boolean
}

export interface DiscountRules {
  counts: { all: number; active: number; pending: number; inactive: number }
  rows: DiscountRuleRow[]
  scopes: {
    id: number
    name: string
    sessions: { id: number; name: string; priceCents: number; startDate: string }[]
  }[]
}

export interface DiscountPreview {
  campers: { registrationId: number; name: string; session: string }[]
  camper: {
    registrationId: number
    name: string
    firstName: string
    session: string
    priceCents: number
    discountCents: number
    finalCents: number
    percent: number
  } | null
  blocked: boolean
  guard: string | null
}

export interface WaiverSummary {
  id: number
  title: string
  program: string
  liveVersion: number
  effectiveDate: string
  signer: string
  signatures: number
  openVersion: { id: number; version: number; status: string } | null
}

export type WaiverVersionStatus = 'Draft' | 'PendingApproval' | 'Published' | 'Archived'

export interface WaiverVersion {
  id: number
  version: number
  body: string
  changeNote: string
  status: WaiverVersionStatus
  statusLabel: string
  effectiveDate: string | null
  retiredDate: string | null
  createdBy: string
  createdAt: string
  submittedBy: string | null
  submittedAt: string | null
  approvedBy: string | null
  approvedAt: string | null
  signatures: number
  editable: boolean
  canApprove: boolean
  approvalBlock: string | null
}

export interface WaiverDetail {
  id: number
  title: string
  program: string
  liveVersion: number
  effectiveDate: string
  signer: string
  signatures: number
  versions: WaiverVersion[]
  recent: { id: number; signerName: string; camper: string; session: string; version: number; acceptedAt: string }[]
}

export interface AuditRow {
  id: number
  createdAt: string
  actor: string
  action: string
  category: string
  what: string
  entityType: string
  entityId: string
  detail: string
  changes: number
}

export interface AuditPage {
  rows: AuditRow[]
  total: number
  page: number
  pages: number
  pageSize: number
  categories: { value: string; label: string }[]
  actors: string[]
}

export interface AuditEntry extends Omit<AuditRow, 'changes'> {
  changes: { field: string; before: string | null; after: string | null }[]
}

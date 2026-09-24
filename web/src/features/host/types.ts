export type VettingStatus = 'NotStarted' | 'InProgress' | 'Approved'
export type RowStatus = 'Valid' | 'Error' | 'Skipped' | 'Submitted'

export interface HostMe {
  organization: string
  city: string
  title: string
  name: string
  email: string
}

export interface VettingCounts {
  total: number
  approved: number
  inProgress: number
  notStarted: number
}

export interface Deadline {
  kind: 'upload' | 'vetting' | 'invoice'
  title: string
  detail: string
  date: string | null
  link: string
  linkLabel: string
}

export interface Overview {
  organization: string
  city: string
  event: {
    program: string
    session: string
    startDate: string
    endDate: string
    location: string
    registrations: number
    capacity: number
    lastYear: number
    vettingDeadline: string
  } | null
  volunteers: VettingCounts
  upload: { needsDecision: number; readyToSubmit: number }
  invoices: {
    balanceCents: number
    openCount: number
    nextDue: {
      id: number
      number: string
      dueDate: string
      balanceCents: number
    } | null
  }
  deadlines: Deadline[]
}

export interface Volunteer {
  id: number
  name: string
  email: string
  phone: string
  role: string
  vettingStatus: VettingStatus
  createdAt: string
  fromUpload: boolean
}

export interface VolunteerList {
  counts: VettingCounts
  roles: string[]
  rows: Volunteer[]
}

export interface VolunteerFields {
  firstName: string
  lastName: string
  email: string
  phone: string
  dateOfBirth: string
  role: string
}

export interface UploadRow extends VolunteerFields {
  id: number
  rowNumber: number
  status: RowStatus
  issue: string | null
  detail: string | null
  field: keyof VolunteerFields | null
}

export interface Upload {
  id: number
  fileName: string
  uploadedBy: string
  createdAt: string
  lastSubmittedAt: string | null
  total: number
  valid: number
  errors: number
  skipped: number
  submitted: number
  rows: UploadRow[]
}

export interface SubmitResult {
  submitted: number
  heldBack: number
  upload: Upload
}

export type InvoiceStatus = 'Balance due' | 'Paid'

export interface InvoiceSummary {
  id: number
  number: string
  description: string
  period: string
  dueDate: string
  totalCents: number
  paidCents: number
  balanceCents: number
  status: InvoiceStatus
  paidOn: string | null
}

export interface InvoicePayment {
  id: number
  amountCents: number
  status: 'Succeeded' | 'Declined'
  cardLast4: string | null
  declineReason: string | null
  paidBy: string
  createdAt: string
}

export interface InvoiceDetail {
  invoice: InvoiceSummary
  organization: string
  city: string
  event: {
    program: string
    startDate: string
    endDate: string
    location: string
  } | null
  issuedOn: string
  lines: { id: number; description: string; amountCents: number }[]
  payments: InvoicePayment[]
}

export interface PayResult {
  outcome: 'Succeeded' | 'Declined'
  amountCents: number
  message: string | null
  cardLast4: string | null
}

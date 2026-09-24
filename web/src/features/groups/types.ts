// Shapes returned by /api/groups and /api/group-links. Money is in cents.

export interface GroupCounts {
  attendees: number
  complete: number
  incomplete: number
  noEmail: number
  withdrawalRequests: number
  withdrawn: number
}

export interface GroupSession {
  id: number
  name: string
  startDate: string
  endDate: string
  priceCents: number
  location: string
}

export interface GroupSummary {
  id: number
  name: string
  status: 'Draft' | 'Confirmed'
  program: string
  session: string
  startDate: string
  endDate: string
  counts: GroupCounts
}

export interface StartContext {
  session: GroupSession
  program: { name: string; slug: string; ministry: string }
  remaining: number
  waivers: string[]
  leaderName: string
  existingGroups: { id: number; name: string; status: string; attendees: number }[]
}

export interface Attendee {
  id: number
  name: string
  email: string | null
  formStatus: 'Complete' | 'Incomplete'
  linkSentAt: string | null
  submittedAt: string | null
  isActive: boolean
  withdrawal: 'None' | 'Requested' | 'Approved' | 'Declined'
  withdrawalReason: string | null
  withdrawalRequestedAt: string | null
}

export interface GroupDetail {
  id: number
  name: string
  status: 'Draft' | 'Confirmed'
  leaderName: string
  program: { name: string; slug: string }
  session: GroupSession
  counts: GroupCounts
  /** Draft: the session price. Confirmed: what was charged per attendee. */
  pricePerAttendeeCents: number
  /** Draft: what paying now would cost. Confirmed: the order total. */
  totalCents: number
  payment: {
    confirmationCode: string
    chargedCents: number
    refundedCents: number
    pricePerAttendeeCents: number
    cardLast4: string | null
    paidAt: string | null
  } | null
  attendees: Attendee[]
}

export interface SentLink {
  attendeeId: number
  name: string
  email: string
  link: string
}

export interface ResendResult {
  sent: SentLink[]
  skipped: string[]
}

export interface LinkQuestion {
  key: string
  label: string
  type: 'Select' | 'Text' | 'YesNo'
  required: boolean
  options: string[]
}

export interface LinkView {
  attendee: {
    name: string
    email: string | null
    phone: string | null
    formStatus: 'Complete' | 'Incomplete'
    submittedAt: string | null
    isActive: boolean
    withdrawal: 'None' | 'Requested' | 'Approved' | 'Declined'
    answers: Record<string, string>
  }
  leaderName: string
  groupName: string
  program: { name: string; location: string }
  session: { name: string; startDate: string; endDate: string }
  waivers: { id: number; title: string; version: number; effectiveDate: string; body: string; accepted: boolean }[]
  questions: LinkQuestion[]
}

/** A roster row being edited in R8. `key` keeps Vue's list identity stable while rows move. */
export interface RosterDraftRow {
  key: number
  name: string
  email: string
}

import type { PoolAvailability } from '@/lib/types'

export interface OpsSession {
  id: number
  name: string
  startDate: string
  endDate: string
  program: string
}

export type Reason = 'Health' | 'Waiver' | 'Balance'

export interface ReadinessRow {
  registrationId: number
  householdId: number
  name: string
  grade: number
  gender: 'Male' | 'Female'
  pool: string
  payment: 'Paid' | 'Balance due'
  balanceCents: number
  waiver: 'Complete' | 'Incomplete' | 'Missing'
  waiversSigned: number
  waiversRequired: number
  health: 'Complete' | 'Incomplete'
  cabin: string
  group: string
  /** Activities by period (O4). NotChosen: the family hasn't ranked any yet. */
  activity: { names: string[]; state: 'Assigned' | 'Partial' | 'Chosen' | 'NotChosen'; label: string }
  reasons: Reason[]
  remindedAt: string | null
  checkedIn: boolean
}

export interface Readiness {
  session: OpsSession & { healthMechanism: string }
  usesCampDoc: boolean
  campDocUrl: string | null
  capacity: number
  registered: number
  waitlisted: number
  ready: number
  needsAttention: number
  breakdown: { health: number; waivers: number; balance: number }
  overlap: { twoReasons: number; threeReasons: number; totalReasons: number }
  pools: PoolAvailability[]
  cabins: string[]
  activities: string[]
  roster: ReadinessRow[]
}

export interface GroupCamper {
  registrationId: number
  name: string
  grade: number
  groupId: number | null
  suggestedGroupId: number | null
  suggestionReason: string | null
  requests: { registrationId: number; name: string; group: string; met: boolean }[]
  reviewReason: string | null
}

export interface GroupBoardView {
  pools: { id: number; name: string; campers: number; needsReview: boolean }[]
  poolId: number | null
  poolName?: string
  groups: { id: number; name: string; capacity: number; count: number }[]
  totals?: { total: number; assigned: number; unassigned: number }
  suggestions?: number
  separated?: {
    a: { registrationId: number; name: string; group: string }
    b: { registrationId: number; name: string; group: string }
  }[]
  campers: GroupCamper[]
}

export type RequestStatus = 'Met' | 'Not met' | 'Cannot be met'

export interface RoomingCamper {
  registrationId: number
  name: string
  grade: number
  gender: 'Male' | 'Female'
  cabinId: number | null
  requests: { with: string; withRegistrationId: number; status: RequestStatus; why: string }[]
  needsReview: boolean
}

export interface RoomingCabin {
  id: number
  name: string
  gender: 'Male' | 'Female'
  beds: number
  taken: number
  requestsMet: number
  requestsNotMet: number
  campers: RoomingCamper[]
}

interface Side {
  cabins: number
  beds: number
  assigned: number
  registered: number
}

export interface Rooming {
  session: OpsSession
  managedInOpera: boolean
  summary: {
    cabins: number
    beds: number
    capacity: number
    registered: number
    assigned: number
    unassigned: number
    boys: Side
    girls: Side
    requestsMet: number
    requestsNotMet: number
    conflicts: number
  }
  review: {
    reviewedAt: string | null
    reviewedBy: string | null
    items: { registrationId: number; name: string; reason: string }[]
  }
  cabins: RoomingCabin[]
  unassigned: RoomingCamper[]
}

export type CheckInStatus = 'Ready' | 'Blocked' | 'Checked in' | 'Checked out'

export interface CheckInRow {
  registrationId: number
  name: string
  grade: number
  gender: 'Male' | 'Female'
  confirmationCode: string
  cabin: string
  activity: string | null
  status: CheckInStatus
  blockers: { kind: Reason; label: string; detail: string }[]
  checkedInAt: string | null
  checkedInBy: string | null
  checkInOverride: string | null
  checkedOutAt: string | null
  checkedOutBy: string | null
  pickedUpBy: string | null
  guardianPhone: string | null
  pickupAdults: { id: number; name: string; relationship: string }[]
}

export interface CheckInBoard {
  session: OpsSession
  capacity: number
  counts: { registered: number; ready: number; blocked: number; checkedIn: number; checkedOut: number }
  rows: CheckInRow[]
}

export function gradeLabel(g: number) {
  return `Grade ${g}`
}

export function genderLabel(g: 'Male' | 'Female') {
  return g === 'Male' ? 'Boy' : 'Girl'
}

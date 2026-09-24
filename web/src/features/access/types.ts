/** Shapes returned by /api/access (Features/Access/AccessEndpoints.cs). */

export interface Ministry {
  id: number
  name: string
}

export interface StaffRow {
  id: number
  name: string
  email: string
  role: string
  roleLabel: string
  ministry: Ministry | null
  healthAccess: boolean
  status: 'Active' | 'Revoked'
  lastSignInAt: string | null
  syncedAt: string | null
  revokedAt: string | null
  /** False for host coordinators, who never see camper health. */
  canViewHealth: boolean
}

export interface SyncRun {
  id: number
  ranAt: string
  actor: string
  members: number
  added: number
  updated: number
  revoked: number
}

export interface StaffList {
  ministries: Ministry[]
  lastSync: SyncRun | null
  syncDue: boolean
  rows: StaffRow[]
}

export interface StaffDetail {
  member: StaffRow
  activity: { id: number; createdAt: string; actor: string; action: string; detail: string }[]
  healthPrograms: { id: number; name: string }[]
}

export type Mechanism = 'Embedded' | 'ThirdParty' | 'CampDoc'

export interface HealthProgram {
  id: number
  name: string
  ministry: Ministry
  type: string
  location: string
  isOvernight: boolean
  mechanism: Mechanism
  viewerRoles: string[]
  thirdPartyFormUrl: string | null
  updatedAt: string | null
  updatedBy: string | null
  sessions: { id: number; name: string; startDate: string; endDate: string }[]
  registered: number
  formsOnFile: number
  healthComplete: number
  viewers: { id: number; name: string; role: string }[]
  blocked: { id: number; name: string; role: string; reason: string }[]
}

export interface HealthSettings {
  roles: { slug: string; label: string }[]
  programs: HealthProgram[]
}

export interface HealthRecord {
  camper: string
  program: string
  mechanism: Mechanism
  status: string
  link: string | null
  message: string | null
  details: {
    allergies: string | null
    medications: string | null
    dietary: string | null
    adaNeeds: string | null
    physicianName: string | null
    physicianPhone: string | null
    insuranceProvider: string | null
  } | null
}

export const mechanisms: { value: Mechanism; label: string; help: string }[] = [
  {
    value: 'Embedded',
    label: 'Embedded form',
    help: 'Families fill in the health form during registration, in this platform.',
  },
  {
    value: 'ThirdParty',
    label: 'Third-party form',
    help: 'Families fill in an outside form, and staff see its link. This platform keeps completion status only.',
  },
  {
    value: 'CampDoc',
    label: 'CampDoc',
    help: 'Health profiles live in CampDoc. This platform keeps completion status only.',
  },
]

export const mechanismLabel = (m: Mechanism) => mechanisms.find((x) => x.value === m)?.label ?? m

// Shapes of the activities API (api/Camp.Api/Features/Activities).

export interface CatalogActivity {
  id: number
  name: string
  category: string
  description: string
  whatToBring: string
  imageUrl: string
  gradeMin: number
  gradeMax: number
  gradeLabel: string
  defaultCapacity: number
  staffRatio: number
  instructor: string
  space: string
  isActive: boolean
  upcomingSlots: number
}

export interface Catalog {
  activities: CatalogActivity[]
  categories: string[]
  images: string[]
  periods: { period: number; time: string }[]
}

export interface ActivityOption {
  activityId: number
  name: string
  capacity: number
  remaining: number
}

export interface PeriodOptions {
  period: number
  time: string
  options: ActivityOption[]
}

export interface CamperOptions {
  personId: number
  firstName: string
  grade: number
  gradeLabel: string
  block: string | null
  periods: PeriodOptions[]
}

export interface ActivityOptions {
  cabinmateLimit: number
  maxRanks: number
  campers: CamperOptions[]
}

export interface ActivityConflict {
  personId: number
  firstName: string
  period: number
  alternatives: { activityId: number; name: string; remaining: number }[]
}

export interface ActivityDetail {
  id: number
  name: string
  category: string
  description: string
  whatToBring: string
  imageUrl: string
  gradeMin: number
  gradeMax: number
  gradeLabel: string
  staffRatio: number
  instructor: string
  space: string
  schedule: {
    blockId: number
    block: string
    grades: string
    session: string
    periods: { period: number; time: string; capacity: number; remaining: number }[]
  }[]
}

/** F1 → choose after registering. */
export interface FamilyActivities {
  registrationId: number
  sessionId: number
  personId: number
  firstName: string
  grade: number
  gradeLabel: string
  block: string
  session: string
  startDate: string
  endDate: string
  periods: PeriodOptions[]
  choices: { period: number; ranked: number[] }[]
  placed: { period: number; activityId: number | null; name: string | null; locked: boolean }[]
  /** Last day the family can change activities (ISO date). */
  changeDeadline: string
  canChange: boolean
}

export type CellStatus = 'Open' | 'Full' | 'Over capacity'
export type ConflictKind = 'OverCapacity' | 'OutsideGrades'

export interface ScheduleCamper {
  registrationId: number
  name: string
  grade: number | null
  choices: string[]
}

export interface ScheduleCell {
  slotId: number
  period: number
  capacity: number
  assigned: number
  status: CellStatus
  instructor: string
  space: string
  campers: ScheduleCamper[]
}

export interface ScheduleRow {
  activityId: number
  name: string
  imageUrl: string | null
  gradeMin: number
  gradeMax: number
  grades: string
  cells: ScheduleCell[]
}

export interface ScheduleConflict {
  kind: ConflictKind
  registrationId: number | null
  camper: string | null
  grade: number | null
  period: number
  slotIds: number[]
  message: string
}

export interface Schedule {
  session: { id: number; name: string; startDate: string; endDate: string }
  blocks: { id: number; name: string; grades: string; campers: number }[]
  block: { id: number; name: string; grades: string; campers: number }
  periods: { period: number; time: string; capacity: number; placed: number; waiting: number; notChosen: number }[]
  rows: ScheduleRow[]
  conflicts: ScheduleConflict[]
  pending: { campers: number; places: number; noRoom: number }
}

export interface AssignResult {
  block: string
  campers: number
  places: number
  noRoom: number
}

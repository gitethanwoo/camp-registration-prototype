// Slice 9 · Registration forms (K6). Shapes returned by /api/admin/forms and the answers readers.

export type FormQuestionType =
  | 'ShortText'
  | 'LongText'
  | 'YesNo'
  | 'SingleChoice'
  | 'MultipleChoice'
  | 'Date'
  | 'Number'
/** Programs without a K6 form still send the original types. */
export type AnyQuestionType = FormQuestionType | 'Select' | 'Text'
export type QuestionScope = 'Participant' | 'Household'
export type FormVersionStatus = 'Draft' | 'PendingApproval' | 'Published' | 'Retired'

export interface FormQuestion {
  key: string
  label: string
  helpText: string | null
  type: AnyQuestionType
  scope: QuestionScope
  required: boolean
  options: string[]
  showWhenKey: string | null
  showWhenValue: string | null
}

export interface ProgramFormRow {
  programId: number
  program: string
  ministry: string
  programPublished: boolean
  live: { id: number; version: number; publishedAt: string; questions: number } | null
  open: { id: number; version: number; status: FormVersionStatus; statusLabel: string } | null
  legacyQuestions: number
}

export interface FormVersionView {
  id: number
  version: number
  status: FormVersionStatus
  statusLabel: string
  changeNote: string
  createdBy: string
  createdAt: string
  updatedAt: string
  submittedBy: string | null
  submittedAt: string | null
  returnNote: string | null
  approvedBy: string | null
  approvedAt: string | null
  publishedAt: string | null
  retiredAt: string | null
  registrations: number
  editable: boolean
  canApprove: boolean
  approvalBlock: string | null
  questions: FormQuestion[]
}

export interface ProgramForms {
  program: {
    id: number
    name: string
    ministry: string
    location: string
    healthMechanism: string
    session: { name: string; startDate: string; endDate: string } | null
  }
  versions: FormVersionView[]
}

export interface AnswerView {
  key: string
  label: string
  type: AnyQuestionType
  value: string
}

export interface StaffAnswers {
  formVersion: number | null
  household: AnswerView[]
  participant: AnswerView[]
}

export interface FamilyAnswers {
  formVersion: number | null
  household: AnswerView[]
  participants: { registrationId: number; firstName: string; answers: AnswerView[] }[]
}

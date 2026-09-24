import { date } from '@/lib/format'
import type { AnswerView, AnyQuestionType, FormQuestion, FormQuestionType } from './types'

// The client mirror of FormRules on the server: which questions a family sees and which are still
// unanswered. The server decides at checkout; this keeps the wizard and the preview honest.

export const typeLabels: Record<FormQuestionType, string> = {
  ShortText: 'Short answer',
  LongText: 'Paragraph',
  YesNo: 'Yes or no',
  SingleChoice: 'Pick one',
  MultipleChoice: 'Pick any',
  Date: 'Date',
  Number: 'Number',
}

export const isChoice = (t: AnyQuestionType) => t === 'SingleChoice' || t === 'MultipleChoice' || t === 'Select'

/** Values a condition on this question can match. */
export function conditionValues(q: FormQuestion) {
  if (q.type === 'YesNo') return ['Yes', 'No']
  return isChoice(q.type) ? q.options : []
}

const matches = (answer: string | undefined, value: string | null) =>
  answer !== undefined && value !== null && (answer === value || answer.split('|').includes(value))

/**
 * The keys a family sees, in order. A question whose condition points at a hidden question is hidden
 * too, so a stale answer can't reveal anything. `known` holds answers from an outer scope (the
 * household's, when working out a camper's questions).
 */
export function visibleKeys(
  questions: FormQuestion[],
  answers: Record<string, string>,
  known: Record<string, string> = {},
) {
  const accepted: Record<string, string> = { ...known }
  const shown = new Set<string>()
  for (const q of questions) {
    if (q.showWhenKey && !matches(accepted[q.showWhenKey], q.showWhenValue)) continue
    shown.add(q.key)
    const a = answers[q.key]?.trim()
    if (a) accepted[q.key] = a
  }
  return shown
}

export function unanswered(
  questions: FormQuestion[],
  answers: Record<string, string>,
  known: Record<string, string> = {},
) {
  const shown = visibleKeys(questions, answers, known)
  return questions.filter((q) => q.required && shown.has(q.key) && !answers[q.key]?.trim())
}

/** Only the answers to questions the family can see. */
export function visibleAnswers(
  questions: FormQuestion[],
  answers: Record<string, string>,
  known: Record<string, string> = {},
) {
  const shown = visibleKeys(questions, answers, known)
  return Object.fromEntries(questions.filter((q) => shown.has(q.key)).map((q) => [q.key, answers[q.key] ?? '']))
}

/** An answer as a reader sees it. */
export function answerText(a: AnswerView) {
  if (!a.value) return '—'
  if (a.type === 'MultipleChoice') return a.value.split('|').join(', ')
  if (a.type === 'Date') return date(a.value)
  return a.value
}

/** A key for a new question from its label: "Swimming ability" → "swimmingAbility". */
export function keyFromLabel(label: string, taken: Set<string>) {
  const words = label
    .replace(/[^A-Za-z0-9 ]/g, ' ')
    .trim()
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 4)
  let base = words
    .map((w, i) => (i === 0 ? w.toLowerCase() : w.charAt(0).toUpperCase() + w.slice(1).toLowerCase()))
    .join('')
  if (!/^[a-z]/.test(base)) base = `q${base}`
  base = base.slice(0, 36) || 'question'
  let key = base
  for (let n = 2; taken.has(key); n++) key = `${base}${n}`
  return key
}

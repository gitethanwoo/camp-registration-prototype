import type { ActivityOption, PeriodOptions } from './types'

// Ranked choices for one camper in one period. The first id is the first choice. Checkout places the
// camper in the highest-ranked choice that still has room, so a full choice only matters when every
// choice is full, but the family should still see that it filled.

export const isFull = (o: ActivityOption) => o.remaining <= 0

export function toggleRank(ranked: number[], id: number, max: number): number[] {
  if (ranked.includes(id)) return ranked.filter((x) => x !== id)
  return ranked.length < max ? [...ranked, id] : ranked
}

/** Ranked choices that have no room left now. */
export function filledChoices(period: PeriodOptions, ranked: number[]): ActivityOption[] {
  return ranked
    .map((id) => period.options.find((o) => o.activityId === id))
    .filter((o) => !!o && isFull(o)) as ActivityOption[]
}

/**
 * What to suggest when a choice fills: the next ranked choice with room, otherwise the open activity
 * with the most room that isn't ranked yet.
 */
export function suggestion(period: PeriodOptions, ranked: number[]): ActivityOption | null {
  const byId = (id: number) => period.options.find((o) => o.activityId === id)
  const next = ranked.map(byId).find((o) => !!o && !isFull(o))
  if (next) return next
  const open = period.options.filter((o) => !isFull(o) && !ranked.includes(o.activityId))
  return open.toSorted((a, b) => b.remaining - a.remaining)[0] ?? null
}

/** Drops the full choices and puts `id` first. */
export function useInstead(period: PeriodOptions, ranked: number[], id: number): number[] {
  const full = new Set(filledChoices(period, ranked).map((o) => o.activityId))
  return [id, ...ranked.filter((x) => x !== id && !full.has(x))]
}

/** Why this period can't be submitted yet, or null. */
export function periodProblem(firstName: string, period: PeriodOptions, ranked: number[]): string | null {
  if (!ranked.length) return `${firstName}: choose an activity for Period ${period.period}.`
  if (filledChoices(period, ranked).length === ranked.length)
    return `${firstName}: every activity you ranked for Period ${period.period} is full. Choose another.`
  return null
}

export function slotsLeft(o: ActivityOption) {
  if (isFull(o)) return 'Full'
  return `${o.remaining} ${o.remaining === 1 ? 'slot' : 'slots'} left`
}

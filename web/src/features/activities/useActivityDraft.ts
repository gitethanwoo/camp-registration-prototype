import { reactive, ref, type Ref } from 'vue'
import { api, ApiError } from '@/lib/api'
import { now } from '@/lib/clock'
import { filledChoices, periodProblem, suggestion } from './ranking'
import type { ActivityConflict, ActivityOptions } from './types'

export interface Cabinmate {
  name: string
  contact: string
}

interface DraftState {
  /** personId → period → ranked activity ids */
  choices: Record<number, Record<number, number[]>>
  cabinmates: Record<number, Cabinmate[]>
}

interface Store {
  draft: DraftState
  options: Ref<ActivityOptions | null>
  loadError: Ref<string | null>
  updatedAt: Ref<Date | null>
  /** Set when checkout answered 409: the campers and periods whose choices all filled. */
  conflicts: Ref<ActivityConflict[]>
  /** personId:index → whether the friend matched a camper in the session. */
  matches: Record<string, boolean | undefined>
}

const stores = new Map<number, Store>()

/**
 * R4 and R5 state for one session's registration wizard, kept in sessionStorage next to the main draft
 * so a refresh keeps the choices. Slots left come from the server; nothing is held until checkout.
 */
export function useActivityDraft(sessionId: number) {
  const key = `draft.activities.${sessionId}`
  let store = stores.get(sessionId)
  if (!store) {
    let initial: DraftState = { choices: {}, cabinmates: {} }
    try {
      const raw = sessionStorage.getItem(key)
      if (raw) initial = { ...initial, ...JSON.parse(raw) }
    } catch {
      /* storage unavailable: choices live in memory only */
    }
    store = {
      draft: reactive(initial),
      options: ref<ActivityOptions | null>(null),
      loadError: ref<string | null>(null),
      updatedAt: ref<Date | null>(null),
      conflicts: ref([]),
      matches: reactive({}),
    }
    stores.set(sessionId, store)
  }
  const s = store

  function save() {
    try {
      sessionStorage.setItem(key, JSON.stringify(s.draft))
    } catch {
      /* ignore */
    }
  }

  async function refresh(personIds: number[]) {
    if (!personIds.length) return
    try {
      s.options.value = await api.get<ActivityOptions>(
        `/sessions/${sessionId}/activity-options?personIds=${personIds.join(',')}`,
      )
      s.loadError.value = null
      s.updatedAt.value = now()
    } catch (e) {
      s.loadError.value =
        e instanceof ApiError ? e.message : "Slots left didn't load. Check your connection; your choices are kept."
    }
  }

  const ranked = (personId: number, period: number) => s.draft.choices[personId]?.[period] ?? []
  function setRanked(personId: number, period: number, ids: number[]) {
    s.draft.choices[personId] = { ...s.draft.choices[personId], [period]: ids }
    s.conflicts.value = s.conflicts.value.filter((c) => c.personId !== personId || c.period !== period)
    save()
  }

  const mates = (personId: number): Cabinmate[] => {
    const limit = s.options.value?.cabinmateLimit ?? 2
    const list = s.draft.cabinmates[personId] ?? []
    return Array.from({ length: limit }, (_, i) => list[i] ?? { name: '', contact: '' })
  }
  function setMate(personId: number, index: number, mate: Cabinmate) {
    const list = mates(personId)
    list[index] = mate
    s.draft.cabinmates[personId] = list
    delete s.matches[`${personId}:${index}`]
    save()
  }
  async function checkMate(personId: number, index: number) {
    const m = mates(personId)[index]
    if (!m?.name.trim() || !m.contact.trim()) return
    try {
      const r = await api.post<{ matched: boolean }>(`/sessions/${sessionId}/cabinmates/check`, m)
      s.matches[`${personId}:${index}`] = r.matched
    } catch {
      delete s.matches[`${personId}:${index}`]
    }
  }

  /** Step errors for R4, per camper and period. The server re-checks at checkout. */
  function activityErrors(people: { id: number; firstName: string }[]) {
    const o = s.options.value
    if (!o) return [s.loadError.value ?? 'Activity choices are still loading.']
    return people.flatMap((p) => {
      const camper = o.campers.find((c) => c.personId === p.id)
      if (!camper?.block) return []
      return camper.periods.map((per) => periodProblem(p.firstName, per, ranked(p.id, per.period))).filter((x) => !!x)
    }) as string[]
  }
  function cabinmateErrors(people: { id: number; firstName: string }[]) {
    return people.flatMap((p) =>
      mates(p.id)
        .filter((m) => !m.name.trim() !== !m.contact.trim())
        .map((m) =>
          m.name.trim()
            ? `${p.firstName}: add a parent's email or a friend code for ${m.name.trim()}, or clear the row.`
            : `${p.firstName}: add the friend's name for ${m.contact.trim()}, or clear the row.`,
        ),
    )
  }

  /** How many ranked choices have filled, across these campers. Continue stops once when this grows. */
  function filledCount(personIds: number[]) {
    const o = s.options.value
    if (!o) return 0
    return o.campers
      .filter((c) => personIds.includes(c.personId))
      .reduce((n, c) => n + c.periods.reduce((m, p) => m + filledChoices(p, ranked(c.personId, p.period)).length, 0), 0)
  }

  /** "Archery · Climbing · Crafts": where each period would place the camper now. */
  function summary(personId: number) {
    const c = s.options.value?.campers.find((x) => x.personId === personId)
    if (!c?.block) return ''
    return c.periods
      .map((p) => (ranked(personId, p.period).length ? suggestion(p, ranked(personId, p.period))?.name : null) ?? '—')
      .join(' · ')
  }

  /** The checkout body for one camper. */
  function payload(personId: number) {
    const periods = s.draft.choices[personId] ?? {}
    return {
      activities: Object.entries(periods)
        .filter(([, ids]) => ids.length)
        .map(([period, ids]) => ({ period: Number(period), ranked: ids })),
      cabinmates: mates(personId)
        .filter((m) => m.name.trim() && m.contact.trim())
        .map((m) => ({ name: m.name.trim(), contact: m.contact.trim() })),
    }
  }

  /** A 409 from checkout: remember which choices filled and reload slots left. False if it wasn't about activities. */
  async function applyConflicts(body: unknown, personIds: number[]) {
    const list = (body as { conflicts?: ActivityConflict[] } | null)?.conflicts
    if (!Array.isArray(list) || !list.length) return false
    s.conflicts.value = list
    await refresh(personIds)
    return true
  }

  function clear() {
    s.draft.choices = {}
    s.draft.cabinmates = {}
    s.conflicts.value = []
    stores.delete(sessionId)
    try {
      sessionStorage.removeItem(key)
    } catch {
      /* ignore */
    }
  }

  return {
    draft: s.draft,
    options: s.options,
    loadError: s.loadError,
    updatedAt: s.updatedAt,
    conflicts: s.conflicts,
    matches: s.matches,
    refresh,
    ranked,
    setRanked,
    mates,
    setMate,
    checkMate,
    activityErrors,
    cabinmateErrors,
    payload,
    filledCount,
    summary,
    applyConflicts,
    clear,
  }
}

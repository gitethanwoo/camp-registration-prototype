import { computed, ref } from 'vue'
import { api } from '@/lib/api'

export interface ScopeMinistry {
  id: number
  code: string
  name: string
  programs: {
    id: number
    name: string
    slug: string
    type: string
    sessions: { id: number; name: string; startDate: string; endDate: string }[]
  }[]
}

const KEY = 'admin.sessionId'
const ministries = ref<ScopeMinistry[]>([])
const sessionId = ref<number | null>(readStored())
let loading: Promise<void> | null = null

function readStored() {
  try {
    const v = localStorage.getItem(KEY)
    return v ? Number(v) : null
  } catch {
    return null
  }
}

async function loadScope() {
  const m = await api.get<ScopeMinistry[]>('/admin/scope')
  ministries.value = m
  const all = m.flatMap((x) => x.programs.flatMap((p) => p.sessions.map((s) => s.id)))
  if (!sessionId.value || !all.includes(sessionId.value)) sessionId.value = all[0] ?? null
}

/** Ministry ▸ Program ▸ Session scope shared by every admin page. */
export function useAdminScope() {
  loading ??= loadScope()

  const current = computed(() => {
    for (const m of ministries.value) {
      for (const p of m.programs) {
        const s = p.sessions.find((x) => x.id === sessionId.value)
        if (s) return { ministry: m, program: p, session: s }
      }
    }
    return null
  })

  function select(id: number) {
    sessionId.value = id
    try {
      localStorage.setItem(KEY, String(id))
    } catch {
      /* storage unavailable: scope just won't persist */
    }
  }

  return { ministries, sessionId, current, select, ready: loading }
}

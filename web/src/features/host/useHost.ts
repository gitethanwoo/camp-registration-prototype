import { ref } from 'vue'
import { api, ApiError } from '@/lib/api'
import type { HostMe } from './types'

const me = ref<HostMe | null>(null)
const notLinked = ref<string | null>(null)
let pending: Promise<void> | null = null

async function load() {
  try {
    me.value = await api.get<HostMe>('/host/me')
    notLinked.value = null
  } catch (e) {
    notLinked.value = e instanceof ApiError ? e.message : "Couldn't load your church."
    pending = null
  }
}

/** The church the signed-in host acts for, loaded once for the shell and pages. */
export function useHost() {
  pending ??= load()
  return { me, notLinked }
}

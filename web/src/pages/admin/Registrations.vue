<script setup lang="ts">
import { refDebounced } from '@vueuse/core'
import { ArrowDown, ArrowUp, ChevronLeft, ChevronRight, Search, X } from '@lucide/vue'
import { computed, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import StatusBadge from '@/components/StatusBadge.vue'
import { useAdminScope } from '@/composables/useAdminScope'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Skeleton } from '@/components/ui/skeleton'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { api } from '@/lib/api'
import { money } from '@/lib/format'

interface Row {
  id: number, participant: string, grade: number, guardian: string, email: string, program: string, session: string, pool: string
  status: string, balance: number, health: string, healthMechanism: string, waiversSigned: number, waiversRequired: number, confirmationCode: string
}
interface Page { total: number, page: number, pageSize: number, rows: Row[] }

const route = useRoute()
const router = useRouter()
const { sessionId, ready } = useAdminScope()

// Filters live in the URL so a filtered view can be shared or bookmarked.
const q = ref((route.query.q as string) ?? '')
const qDebounced = refDebounced(q, 250)
const allSessions = ref(route.query.all === '1')
const status = ref((route.query.status as string) ?? 'any')
const attention = ref((route.query.attention as string) ?? 'any')
const pool = ref((route.query.pool as string) ?? 'any')
const sort = ref((route.query.sort as string) ?? 'participant')
const dir = ref((route.query.dir as string) ?? 'asc')
const page = ref(Number(route.query.page ?? 1))

const data = ref<Page | null>(null)
const loading = ref(false)
const pools = ref<{ id: number, name: string }[]>([])

const params = computed(() => {
  const p: Record<string, string> = { sort: sort.value, dir: dir.value, page: String(page.value), pageSize: '25' }
  if (!allSessions.value && sessionId.value) p.sessionId = String(sessionId.value)
  if (qDebounced.value.trim()) p.q = qDebounced.value.trim()
  if (status.value !== 'any') p.status = status.value
  if (attention.value !== 'any') p.attention = attention.value
  if (pool.value !== 'any' && !allSessions.value) p.poolId = pool.value
  return p
})

async function load() {
  loading.value = true
  try { data.value = await api.get<Page>(`/admin/registrations?${new URLSearchParams(params.value)}`) }
  finally { loading.value = false }
  const { sessionId: _s, pageSize: _p, poolId, ...rest } = params.value
  router.replace({ query: { ...rest, ...(poolId ? { pool: poolId } : {}), ...(allSessions.value ? { all: '1' } : {}) } })
}

onMounted(async () => {
  await ready
  if (sessionId.value) {
    const s = await api.get<{ pools: { id: number, name: string }[] }>(`/admin/sessions/${sessionId.value}`)
    pools.value = s.pools
  }
  await load()
})
watch([qDebounced, status, attention, pool, allSessions], () => { page.value = 1 })
// The header's "Search families" link reuses this page with ?all=1.
watch(() => route.query.all, (v) => { if (v === '1') allSessions.value = true })
watch(params, load)

function sortBy(col: string) {
  if (sort.value === col) dir.value = dir.value === 'asc' ? 'desc' : 'asc'
  else { sort.value = col; dir.value = col === 'balance' ? 'desc' : 'asc' }
}
const ariaSort = (col: string) => sort.value === col ? (dir.value === 'asc' ? 'ascending' : 'descending') : 'none'
const pages = computed(() => data.value ? Math.max(1, Math.ceil(data.value.total / data.value.pageSize)) : 1)
const filtered = computed(() => !!(q.value || status.value !== 'any' || attention.value !== 'any' || pool.value !== 'any'))
function reset() { q.value = ''; status.value = 'any'; attention.value = 'any'; pool.value = 'any' }

// One datum per column. Contact details live on the registration page.
const cols = computed(() => [
  { key: 'participant', label: 'Participant' },
  { key: null, label: 'Guardian' },
  ...(allSessions.value ? [{ key: null, label: 'Session' }] : []),
  { key: 'grade', label: 'Grade' },
  { key: null, label: 'Group' },
  { key: 'status', label: 'Status' },
  { key: null, label: 'Health' },
  { key: null, label: 'Waivers' },
  { key: 'balance', label: 'Balance', right: true },
] as { key: string | null, label: string, right?: boolean }[])
</script>

<template>
  <div class="space-y-4">
    <div class="flex flex-wrap items-center gap-2">
      <div class="relative w-full sm:w-72">
        <Search class="absolute top-1/2 left-2.5 size-4 -translate-y-1/2 text-muted-foreground" />
        <Input v-model="q" placeholder="Name, email, phone, or confirmation" class="pl-8" aria-label="Search registrations" />
      </div>
      <Select v-model="status">
        <SelectTrigger class="w-40" aria-label="Status"><SelectValue /></SelectTrigger>
        <SelectContent>
          <SelectItem value="any">Any status</SelectItem>
          <SelectItem value="Confirmed">Confirmed</SelectItem>
          <SelectItem value="PaymentPending">Payment pending</SelectItem>
          <SelectItem value="Cancelled">Cancelled</SelectItem>
        </SelectContent>
      </Select>
      <Select v-model="attention">
        <SelectTrigger class="w-44" aria-label="Needs attention"><SelectValue /></SelectTrigger>
        <SelectContent>
          <SelectItem value="any">All campers</SelectItem>
          <SelectItem value="health">Health incomplete</SelectItem>
          <SelectItem value="waivers">Waivers missing</SelectItem>
          <SelectItem value="balance">Balance due</SelectItem>
        </SelectContent>
      </Select>
      <Select v-if="!allSessions && pools.length" v-model="pool">
        <SelectTrigger class="w-40" aria-label="Group"><SelectValue /></SelectTrigger>
        <SelectContent>
          <SelectItem value="any">All groups</SelectItem>
          <SelectItem v-for="p in pools" :key="p.id" :value="String(p.id)">{{ p.name }}</SelectItem>
        </SelectContent>
      </Select>
      <Button v-if="filtered" variant="ghost" size="sm" @click="reset"><X class="size-4" />Reset</Button>
      <label class="ml-auto flex items-center gap-2 text-sm text-muted-foreground">
        <input v-model="allSessions" type="checkbox" class="size-4 accent-primary">
        Search all ministries
      </label>
    </div>

    <div class="rounded-lg border">
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead v-for="c in cols" :key="c.label" :aria-sort="c.key ? ariaSort(c.key) : undefined" :class="c.right ? 'text-right' : ''">
              <button v-if="c.key" class="inline-flex items-center gap-1 hover:text-foreground" @click="sortBy(c.key)">
                {{ c.label }}
                <ArrowUp v-if="sort === c.key && dir === 'asc'" class="size-3.5" />
                <ArrowDown v-else-if="sort === c.key" class="size-3.5" />
              </button>
              <template v-else>{{ c.label }}</template>
            </TableHead>
          </TableRow>
        </TableHeader>
        <TableBody :class="loading && 'opacity-60'">
          <template v-if="!data">
            <TableRow v-for="i in 8" :key="i"><TableCell :colspan="cols.length"><Skeleton class="h-5" /></TableCell></TableRow>
          </template>
          <TableRow v-else-if="!data.rows.length">
            <TableCell :colspan="cols.length" class="h-24 text-center text-muted-foreground">No registrations match these filters.</TableCell>
          </TableRow>
          <TableRow
            v-for="r in data?.rows" :key="r.id"
            class="cursor-pointer"
            tabindex="0"
            @click="router.push(`/admin/registrations/${r.id}`)"
            @keydown.enter="router.push(`/admin/registrations/${r.id}`)"
          >
            <TableCell class="font-medium">{{ r.participant }}</TableCell>
            <TableCell class="text-muted-foreground">{{ r.guardian }}</TableCell>
            <TableCell v-if="allSessions" class="whitespace-nowrap">{{ r.program }} · {{ r.session }}</TableCell>
            <TableCell class="tabular-nums">{{ r.grade }}</TableCell>
            <TableCell>{{ r.pool }}</TableCell>
            <TableCell><StatusBadge :status="r.status" /></TableCell>
            <TableCell><StatusBadge :status="r.health" :label="r.healthMechanism === 'CampDoc' && r.health === 'Incomplete' ? 'CampDoc incomplete' : undefined" /></TableCell>
            <TableCell :class="r.waiversSigned < r.waiversRequired ? 'font-medium text-destructive' : 'text-muted-foreground'" class="tabular-nums">{{ r.waiversSigned }}/{{ r.waiversRequired }}</TableCell>
            <TableCell class="text-right tabular-nums" :class="r.balance > 0 ? 'font-medium' : 'text-muted-foreground'">{{ r.balance > 0 ? money(r.balance) : '—' }}</TableCell>
          </TableRow>
        </TableBody>
      </Table>
    </div>

    <div v-if="data" class="flex items-center justify-between text-sm text-muted-foreground">
      <span>{{ data.total }} {{ data.total === 1 ? 'registration' : 'registrations' }}</span>
      <div class="flex items-center gap-2">
        <span>Page {{ page }} of {{ pages }}</span>
        <Button variant="outline" size="icon" class="size-8" :disabled="page <= 1" aria-label="Previous page" @click="page--"><ChevronLeft class="size-4" /></Button>
        <Button variant="outline" size="icon" class="size-8" :disabled="page >= pages" aria-label="Next page" @click="page++"><ChevronRight class="size-4" /></Button>
      </div>
    </div>
  </div>
</template>

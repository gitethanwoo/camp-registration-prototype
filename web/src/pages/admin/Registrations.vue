<script setup lang="ts">
import { refDebounced } from '@vueuse/core'
import type { ColumnDef, SortingState } from '@tanstack/vue-table'
import { ChevronLeft, ChevronRight, Search, X } from '@lucide/vue'
import { computed, h, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import StatusBadge from '@/components/StatusBadge.vue'
import { useAdminScope } from '@/composables/useAdminScope'
import { Button } from '@/components/ui/button'
import { Checkbox } from '@/components/ui/checkbox'
import { DataTable, DataTableColumnHeader } from '@/components/ui/data-table'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
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

// Sorting happens server-side; the table just reads and writes sort/dir.
const sorting = computed<SortingState>({
  get: () => [{ id: sort.value, desc: dir.value === 'desc' }],
  set: ([s]) => { if (s) { sort.value = s.id; dir.value = s.desc ? 'desc' : 'asc' } },
})
const pages = computed(() => data.value ? Math.max(1, Math.ceil(data.value.total / data.value.pageSize)) : 1)
const filtered = computed(() => !!(q.value || status.value !== 'any' || attention.value !== 'any' || pool.value !== 'any'))
function reset() { q.value = ''; status.value = 'any'; attention.value = 'any'; pool.value = 'any' }

const sortable = (title: string): ColumnDef<Row>['header'] => ({ column }) => h(DataTableColumnHeader<Row>, { column, title })

// One datum per column. Contact details live on the registration page.
const columns = computed<ColumnDef<Row>[]>(() => [
  { accessorKey: 'participant', header: sortable('Participant'), enableSorting: true, meta: { cellClass: 'font-medium' } },
  { accessorKey: 'guardian', header: 'Guardian', meta: { cellClass: 'text-muted-foreground' } },
  ...(allSessions.value
    ? [{ id: 'session', header: 'Session', cell: ({ row }) => `${row.original.program} · ${row.original.session}`, meta: { cellClass: 'whitespace-nowrap' } } satisfies ColumnDef<Row>]
    : []),
  { accessorKey: 'grade', header: sortable('Grade'), enableSorting: true, meta: { cellClass: 'tabular-nums' } },
  { accessorKey: 'pool', header: 'Group' },
  { accessorKey: 'status', header: sortable('Status'), enableSorting: true },
  { accessorKey: 'health', header: 'Health' },
  {
    id: 'waivers', header: 'Waivers',
    cell: ({ row }) => `${row.original.waiversSigned}/${row.original.waiversRequired}`,
    meta: { cellClass: r => ['tabular-nums', r.waiversSigned < r.waiversRequired ? 'font-medium text-destructive' : 'text-muted-foreground'] },
  },
  {
    accessorKey: 'balance', header: sortable('Balance'), enableSorting: true, sortDescFirst: true,
    cell: ({ row }) => row.original.balance > 0 ? money(row.original.balance) : '—',
    meta: { class: 'text-right', cellClass: r => ['tabular-nums', r.balance > 0 ? 'font-medium' : 'text-muted-foreground'] },
  },
])
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
      <div class="ml-auto flex items-center gap-2">
        <Checkbox id="all-sessions" v-model="allSessions" />
        <Label for="all-sessions" class="font-normal text-muted-foreground">Search all ministries</Label>
      </div>
    </div>

    <DataTable
      v-model:sorting="sorting"
      :columns="columns"
      :data="data?.rows"
      :loading="loading"
      manual-sorting
      :skeleton-rows="8"
      :get-row-id="r => String(r.id)"
      empty-text="No registrations match these filters."
      @row-click="r => router.push(`/admin/registrations/${r.id}`)"
    >
      <template #cell-status="{ row }"><StatusBadge :status="row.status" /></template>
      <template #cell-health="{ row }">
        <StatusBadge :status="row.health" :label="row.healthMechanism === 'CampDoc' && row.health === 'Incomplete' ? 'CampDoc incomplete' : undefined" />
      </template>
    </DataTable>

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

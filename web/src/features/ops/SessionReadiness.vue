<script setup lang="ts">
import {
  CircleCheck,
  CreditCard,
  Download,
  EllipsisVertical,
  ExternalLink,
  FileSignature,
  HeartPulse,
  Hourglass,
  House,
  Search,
  Send,
  TriangleAlert,
  Users,
} from '@lucide/vue'
import type { ColumnDef, SortingState } from '@tanstack/vue-table'
import { computed, h, ref, watch } from 'vue'
import { RouterLink } from 'vue-router'
import { toast } from 'vue-sonner'
import StatusBadge from '@/components/StatusBadge.vue'
import { Alert, AlertDescription } from '@/components/ui/alert'
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from '@/components/ui/alert-dialog'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Checkbox } from '@/components/ui/checkbox'
import { Progress } from '@/components/ui/progress'
import { DataTable, DataTableColumnHeader } from '@/components/ui/data-table'
import { DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger } from '@/components/ui/dropdown-menu'
import { Input } from '@/components/ui/input'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Separator } from '@/components/ui/separator'
import { Skeleton } from '@/components/ui/skeleton'
import { api } from '@/lib/api'
import { dateRange, dateTime, money } from '@/lib/format'
import { downloadCsv } from './csv'
import StatTile from './StatTile.vue'
import type { Readiness, ReadinessRow, Reason } from './types'
import { genderLabel } from './types'
import { describe, useOpsData } from './useOpsData'

// O1 · Session readiness: who isn't ready for camp and why, with reminders that go out as HubSpot emails.
const {
  data: r,
  error,
  load,
  sessionId,
  canEdit,
} = useOpsData<Readiness>((id) => `/admin/ops/sessions/${id}/readiness`)

type StatusFilter = 'all' | 'ready' | 'attention' | Reason
const search = ref('')
const cabin = ref('all')
const activity = ref('all')
const status = ref<StatusFilter>('all')
const page = ref(0)
const pageSize = 25
const selected = ref(new Set<number>())

const healthLabel = computed(() => (r.value?.usesCampDoc ? 'CampDoc' : 'Health form'))
const pct = (n: number, of: number) => (of ? Math.round((n / of) * 100) : 0)

const filtered = computed(() => {
  const q = search.value.trim().toLowerCase()
  return (r.value?.roster ?? []).filter((x) => {
    if (q && !x.name.toLowerCase().includes(q)) return false
    if (cabin.value !== 'all' && x.cabin !== cabin.value) return false
    if (activity.value === 'none' && x.activity) return false
    if (activity.value !== 'all' && activity.value !== 'none' && x.activity !== activity.value) return false
    if (status.value === 'ready') return x.reasons.length === 0
    if (status.value === 'attention') return x.reasons.length > 0
    if (status.value !== 'all') return x.reasons.includes(status.value)
    return true
  })
})
watch([search, cabin, activity, status], () => {
  page.value = 0
})
// Sorting runs over every filtered row, not just the visible page.
const sorting = ref<SortingState>([{ id: 'camper', desc: false }])
const byCamper = (x: ReadinessRow) => `${x.name.split(' ').at(-1)} ${x.name}`
const sortKeys: Record<string, (x: ReadinessRow) => string> = { camper: byCamper, cabin: (x) => x.cabin }
const sorted = computed(() => {
  const s = sorting.value[0]
  const key = sortKeys[s?.id ?? 'camper'] ?? byCamper
  const dir = s?.desc ? -1 : 1
  return filtered.value.toSorted((a, b) => {
    const ka = key(a)
    const kb = key(b)
    if (ka === kb) return 0
    return ka < kb ? -dir : dir
  })
})
watch(sorting, () => {
  page.value = 0
})
const pages = computed(() => Math.max(1, Math.ceil(filtered.value.length / pageSize)))
const pageRows = computed(() => sorted.value.slice(page.value * pageSize, (page.value + 1) * pageSize))
const rangeLabel = computed(() => {
  const n = filtered.value.length
  if (!n) return '0 campers'
  const from = page.value * pageSize + 1
  return `${from}–${Math.min(n, from + pageSize - 1)} of ${n} ${n === 1 ? 'camper' : 'campers'}`
})

const allOnPage = computed(
  () => pageRows.value.length > 0 && pageRows.value.every((x) => selected.value.has(x.registrationId)),
)
function toggle(id: number, on: boolean | 'indeterminate') {
  const next = new Set(selected.value)
  if (on === true) next.add(id)
  else next.delete(id)
  selected.value = next
}
function togglePage(on: boolean | 'indeterminate') {
  const next = new Set(selected.value)
  for (const x of pageRows.value) {
    if (on === true) next.add(x.registrationId)
    else next.delete(x.registrationId)
  }
  selected.value = next
}

const columns: ColumnDef<ReadinessRow>[] = [
  {
    id: 'select',
    header: () =>
      h(Checkbox, {
        modelValue: allOnPage.value,
        'aria-label': 'Select every camper on this page',
        'onUpdate:modelValue': togglePage,
      }),
    meta: { class: 'w-10' },
  },
  {
    id: 'camper',
    accessorFn: (x) => x.name,
    header: ({ column }) => h(DataTableColumnHeader<ReadinessRow>, { column, title: 'Camper' }),
    enableSorting: true,
  },
  { id: 'payment', header: 'Payment', meta: { class: 'hidden md:table-cell' } },
  { id: 'waiver', header: 'Waiver', meta: { class: 'hidden md:table-cell' } },
  { id: 'health', header: 'CampDoc', meta: { class: 'hidden md:table-cell' } },
  {
    accessorKey: 'cabin',
    header: ({ column }) => h(DataTableColumnHeader<ReadinessRow>, { column, title: 'Cabin' }),
    enableSorting: true,
    meta: { class: 'hidden lg:table-cell' },
  },
  { id: 'activity', header: 'Activity', meta: { class: 'hidden xl:table-cell' } },
  { id: 'actions', header: () => h('span', { class: 'sr-only' }, 'Actions'), meta: { class: 'w-10' } },
]

// Row action: remind just this camper's family.
function remindOne(x: ReadinessRow) {
  selected.value = new Set([x.registrationId])
  reminderOpen.value = true
}

// Reminders go to the chosen campers who still have something open, or to everyone who needs attention.
const reminderOpen = ref(false)
const sending = ref(false)
const reminderTargets = computed(() => {
  const roster = r.value?.roster ?? []
  const pool = selected.value.size ? roster.filter((x) => selected.value.has(x.registrationId)) : roster
  return pool.filter((x) => x.reasons.length > 0)
})
const skippedReady = computed(() => (selected.value.size ? selected.value.size - reminderTargets.value.length : 0))
async function sendReminders() {
  const id = sessionId.value
  if (!id) return
  sending.value = true
  try {
    const res = await api.post<{ families: number; campers: number; skipped: number }>(
      `/admin/ops/sessions/${id}/reminders`,
      { registrationIds: reminderTargets.value.map((x) => x.registrationId) },
    )
    toast.success(
      `Reminders queued for ${res.families} ${res.families === 1 ? 'family' : 'families'} (${res.campers} ${res.campers === 1 ? 'camper' : 'campers'}).`,
      { description: 'HubSpot sends each family one email listing what is still open.' },
    )
    selected.value = new Set()
    await load()
  } catch (e) {
    toast.error(describe(e, "Reminders weren't sent. Try again."))
  } finally {
    sending.value = false
  }
}

function exportRoster() {
  const x = r.value
  if (!x) return
  downloadCsv(
    `${x.session.program}-${x.session.name}-roster.csv`.replaceAll(' ', '-').toLowerCase(),
    [
      'Camper',
      'Grade',
      'Gender',
      'Pool',
      'Payment',
      'Balance',
      'Waiver',
      healthLabel.value,
      'Cabin',
      'Group',
      'Activity',
    ],
    filtered.value.map((c) => [
      c.name,
      c.grade,
      genderLabel(c.gender),
      c.pool,
      c.payment,
      money(c.balanceCents),
      c.waiver,
      c.health,
      c.cabin,
      c.group,
      c.activity ?? 'Not chosen',
    ]),
  )
}
function showOnly(s: StatusFilter) {
  status.value = s
}
</script>

<template>
  <div class="mx-auto max-w-7xl space-y-6">
    <Alert v-if="error" variant="destructive"
      ><AlertDescription>{{ error }}</AlertDescription></Alert
    >
    <template v-else-if="!r">
      <Skeleton class="h-10 w-72" />
      <div class="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <Skeleton v-for="i in 4" :key="i" class="h-28 rounded-xl" />
      </div>
    </template>
    <template v-else>
      <div>
        <h1 class="text-2xl font-semibold tracking-tight">Session readiness</h1>
        <p class="text-muted-foreground">
          {{ r.session.program }} · {{ r.session.name }} · {{ dateRange(r.session.startDate, r.session.endDate) }}
        </p>
      </div>

      <div class="grid grid-cols-2 gap-3 sm:gap-4 xl:grid-cols-4">
        <StatTile
          label="Registered"
          :value="r.registered"
          :of="r.capacity"
          :icon="Users"
          :percent="pct(r.registered, r.capacity)"
          :hint="`${pct(r.registered, r.capacity)}% full`"
        />
        <StatTile label="Waitlisted" :value="r.waitlisted" :icon="Hourglass" hint="Waiting for a spot to open" />
        <StatTile
          label="Ready"
          :value="r.ready"
          :icon="CircleCheck"
          tone="good"
          :hint="`${pct(r.ready, r.registered)}% of registered`"
        />
        <StatTile
          label="Needs attention"
          :value="r.needsAttention"
          :icon="TriangleAlert"
          tone="warn"
          :hint="`${pct(r.needsAttention, r.registered)}% of registered`"
        />
      </div>

      <div class="grid gap-6 lg:grid-cols-[minmax(0,1fr)_20rem]">
        <section class="min-w-0 space-y-3" aria-label="Roster">
          <div class="flex flex-wrap gap-2">
            <div class="relative w-full sm:w-48">
              <Search class="absolute top-2.5 left-2.5 size-4 text-muted-foreground" aria-hidden="true" />
              <Input v-model="search" class="pl-8" placeholder="Search campers" aria-label="Search campers" />
            </div>
            <Select v-model="cabin">
              <SelectTrigger class="w-full sm:w-36" aria-label="Filter by cabin"><SelectValue /></SelectTrigger>
              <SelectContent>
                <SelectItem value="all">All cabins</SelectItem>
                <SelectItem value="Unassigned">Unassigned</SelectItem>
                <SelectItem v-for="c in r.cabins" :key="c" :value="c">{{ c }}</SelectItem>
              </SelectContent>
            </Select>
            <Select v-model="activity">
              <SelectTrigger class="w-full sm:w-36" aria-label="Filter by activity"><SelectValue /></SelectTrigger>
              <SelectContent>
                <SelectItem value="all">All activities</SelectItem>
                <SelectItem value="none">Not chosen</SelectItem>
                <SelectItem v-for="a in r.activities" :key="a" :value="a">{{ a }}</SelectItem>
              </SelectContent>
            </Select>
            <Select v-model="status">
              <SelectTrigger class="w-full sm:w-44" aria-label="Filter by status"><SelectValue /></SelectTrigger>
              <SelectContent>
                <SelectItem value="all">All statuses</SelectItem>
                <SelectItem value="ready">Ready</SelectItem>
                <SelectItem value="attention">Needs attention</SelectItem>
                <SelectItem value="Health">{{ healthLabel }} incomplete</SelectItem>
                <SelectItem value="Waiver">Waiver missing</SelectItem>
                <SelectItem value="Balance">Balance due</SelectItem>
              </SelectContent>
            </Select>
            <Button variant="outline" class="w-full sm:ml-auto sm:w-auto" @click="exportRoster">
              <Download class="size-4" />Export
            </Button>
          </div>

          <DataTable
            v-model:sorting="sorting"
            manual-sorting
            :columns="columns"
            :data="pageRows"
            :get-row-id="(x) => String(x.registrationId)"
            empty-text="No campers match these filters."
          >
            <template #cell-select="{ row: x }">
              <Checkbox
                :model-value="selected.has(x.registrationId)"
                :aria-label="`Select ${x.name}`"
                @update:model-value="(v) => toggle(x.registrationId, v)"
              />
            </template>
            <template #cell-camper="{ row: x }">
              <div class="font-medium">{{ x.name }}</div>
              <div class="text-sm text-muted-foreground">Grade {{ x.grade }} · {{ genderLabel(x.gender) }}</div>
              <div class="mt-1 flex flex-wrap gap-1 md:hidden">
                <StatusBadge v-if="x.reasons.length === 0" status="Complete" label="Ready" />
                <StatusBadge v-if="x.reasons.includes('Balance')" status="Incomplete" label="Balance due" />
                <StatusBadge v-if="x.reasons.includes('Waiver')" status="Missing" label="Waiver missing" />
                <StatusBadge
                  v-if="x.reasons.includes('Health')"
                  status="Incomplete"
                  :label="`${healthLabel} incomplete`"
                />
              </div>
              <div v-if="x.remindedAt" class="mt-1 text-xs text-muted-foreground">
                Reminded {{ dateTime(x.remindedAt) }}
              </div>
            </template>
            <template #cell-payment="{ row: x }">
              <StatusBadge :status="x.payment === 'Paid' ? 'Paid' : 'Incomplete'" :label="x.payment" />
              <div v-if="x.balanceCents > 0" class="mt-1 text-xs text-muted-foreground tabular-nums">
                {{ money(x.balanceCents) }} due
              </div>
            </template>
            <template #cell-waiver="{ row: x }"><StatusBadge :status="x.waiver" /></template>
            <template #cell-health="{ row: x }">
              <StatusBadge :status="x.health" />
              <a
                v-if="r.campDocUrl && x.health === 'Incomplete'"
                :href="r.campDocUrl"
                target="_blank"
                rel="noopener"
                class="mt-1 flex items-center gap-1 text-xs font-medium underline underline-offset-4"
                >Open CampDoc<ExternalLink class="size-3" aria-hidden="true"
              /></a>
            </template>
            <template #cell-actions="{ row: x }">
              <DropdownMenu>
                <DropdownMenuTrigger as-child>
                  <Button variant="ghost" size="icon" class="size-8" :aria-label="`Actions for ${x.name}`">
                    <EllipsisVertical class="size-4" />
                  </Button>
                </DropdownMenuTrigger>
                <DropdownMenuContent align="end">
                  <DropdownMenuItem as-child>
                    <RouterLink :to="`/admin/households/${x.householdId}`"
                      ><House class="size-4" />Open household</RouterLink
                    >
                  </DropdownMenuItem>
                  <DropdownMenuItem v-if="canEdit" :disabled="x.reasons.length === 0" @select="remindOne(x)">
                    <Send class="size-4" />{{ x.reasons.length ? 'Send a reminder' : 'Ready, nothing to remind' }}
                  </DropdownMenuItem>
                  <DropdownMenuItem v-if="r.campDocUrl && x.health === 'Incomplete'" as-child>
                    <a :href="r.campDocUrl" target="_blank" rel="noopener"
                      ><ExternalLink class="size-4" />Open CampDoc</a
                    >
                  </DropdownMenuItem>
                </DropdownMenuContent>
              </DropdownMenu>
            </template>
            <template #cell-activity="{ row: x }">
              <span :class="x.activity ? '' : 'text-muted-foreground'">{{ x.activity ?? 'Not chosen' }}</span>
            </template>
          </DataTable>

          <div class="flex flex-wrap items-center justify-between gap-2 text-sm">
            <span class="text-muted-foreground">
              {{ rangeLabel }}<template v-if="selected.size"> · {{ selected.size }} selected</template>
            </span>
            <div class="flex items-center gap-2">
              <Button variant="outline" size="sm" :disabled="page === 0" @click="page--">Previous</Button>
              <span class="tabular-nums">Page {{ page + 1 }} of {{ pages }}</span>
              <Button variant="outline" size="sm" :disabled="page + 1 >= pages" @click="page++">Next</Button>
            </div>
          </div>
        </section>

        <aside class="order-first space-y-4 lg:order-none">
          <Card>
            <CardHeader>
              <CardTitle
                >Needs attention · {{ r.needsAttention }} {{ r.needsAttention === 1 ? 'camper' : 'campers' }}</CardTitle
              >
            </CardHeader>
            <CardContent class="space-y-3">
              <ul class="space-y-1">
                <li>
                  <Button variant="ghost" class="w-full justify-start" @click="showOnly('Health')">
                    <HeartPulse class="size-4" />{{ r.breakdown.health }} {{ healthLabel }} incomplete
                  </Button>
                </li>
                <li>
                  <Button variant="ghost" class="w-full justify-start" @click="showOnly('Waiver')">
                    <FileSignature class="size-4" />{{ r.breakdown.waivers }}
                    {{ r.breakdown.waivers === 1 ? 'waiver' : 'waivers' }} missing
                  </Button>
                </li>
                <li>
                  <Button variant="ghost" class="w-full justify-start" @click="showOnly('Balance')">
                    <CreditCard class="size-4" />{{ r.breakdown.balance }} balance due
                  </Button>
                </li>
              </ul>
              <Separator />
              <p class="text-sm text-muted-foreground" data-testid="overlap-note">
                Reasons overlap: {{ r.overlap.totalReasons }} open items across {{ r.needsAttention }} unique campers.
                <template v-if="r.overlap.twoReasons"> {{ r.overlap.twoReasons }} have two.</template>
                <template v-if="r.overlap.threeReasons"> {{ r.overlap.threeReasons }} have all three.</template>
              </p>
              <Button
                v-if="canEdit"
                class="w-full"
                :disabled="reminderTargets.length === 0 || sending"
                @click="reminderOpen = true"
              >
                <Send class="size-4" />
                {{ selected.size ? `Send reminders (${reminderTargets.length})` : 'Send reminders' }}
              </Button>
            </CardContent>
          </Card>

          <Card>
            <CardHeader><CardTitle>Capacity pools</CardTitle></CardHeader>
            <CardContent>
              <ul class="space-y-3" data-testid="capacity-pools">
                <li v-for="p in r.pools" :key="p.id" class="space-y-1">
                  <div class="flex items-center justify-between gap-2 text-sm">
                    <span>{{ p.name }}</span>
                    <span class="flex items-center gap-2 tabular-nums">
                      {{ p.reserved }} / {{ p.capacity }}
                      <Badge
                        v-if="p.state === 'full'"
                        variant="outline"
                        class="border-amber-300 bg-amber-50 text-amber-800"
                      >
                        Full
                      </Badge>
                    </span>
                  </div>
                  <Progress
                    :model-value="pct(p.reserved, p.capacity)"
                    :aria-label="`${p.name}: ${p.reserved} of ${p.capacity} taken`"
                    class="h-1.5 [&>*]:bg-emerald-600"
                  />
                  <p v-if="p.waitlisted" class="text-right text-xs text-muted-foreground">
                    Waitlist: {{ p.waitlisted }}
                  </p>
                </li>
              </ul>
              <Separator class="my-3" />
              <p class="flex justify-between text-sm font-medium">
                <span>Total registered</span><span class="tabular-nums">{{ r.registered }} / {{ r.capacity }}</span>
              </p>
            </CardContent>
          </Card>
        </aside>
      </div>
    </template>

    <AlertDialog v-model:open="reminderOpen">
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>
            Send reminders about {{ reminderTargets.length }} {{ reminderTargets.length === 1 ? 'camper' : 'campers' }}?
          </AlertDialogTitle>
          <AlertDialogDescription>
            Each family gets one email from HubSpot listing what is still open for each of their campers.
            <template v-if="skippedReady">
              {{ skippedReady }} selected {{ skippedReady === 1 ? 'camper is' : 'campers are' }} already ready and won't
              get one.</template
            >
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel>Cancel</AlertDialogCancel>
          <AlertDialogAction @click="sendReminders">Send reminders</AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  </div>
</template>

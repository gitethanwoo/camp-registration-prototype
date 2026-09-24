<script setup lang="ts">
import { BadgeCheck, CalendarCheck, Download, DollarSign, Info, Users, UsersRound } from '@lucide/vue'
import type { ColumnDef } from '@tanstack/vue-table'
import { computed, onMounted, ref, watch } from 'vue'
import { RouterLink } from 'vue-router'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { DataTable } from '@/components/ui/data-table'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Progress } from '@/components/ui/progress'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Skeleton } from '@/components/ui/skeleton'
import { api, ApiError } from '@/lib/api'
import { date, money } from '@/lib/format'
import StatTile from './StatTile.vue'
import type { Report, ReportRow } from './types'
import { now } from '@/lib/clock'

// FN1 · Registration, revenue and attendance reporting. Settled revenue is the certified metric: it is summed from
// the same Fiserv settlement lines Reconciliation (FN2) shows, so the two screens agree to the cent (FR-107).
const report = ref<Report | null>(null)
const loading = ref(false)
const loadError = ref<string | null>(null)

const ministry = ref('all')
const program = ref('all')
const session = ref('all')
const preset = ref<'30' | '90' | 'ytd' | 'custom'>('90')
const customFrom = ref('')
const customTo = ref('')

// Local calendar dates, so an evening in Georgia doesn't roll the range into tomorrow (UTC).
const iso = (d: Date) =>
  `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
const range = computed(() => {
  const today = now()
  if (preset.value === 'custom') return { from: customFrom.value, to: customTo.value }
  if (preset.value === 'ytd') return { from: `${today.getFullYear()}-01-01`, to: iso(today) }
  const days = preset.value === '30' ? 29 : 89
  return { from: iso(new Date(today.getTime() - days * 86_400_000)), to: iso(today) }
})

const query = computed(() => {
  const p = new URLSearchParams()
  if (ministry.value !== 'all') p.set('ministryId', ministry.value)
  if (program.value !== 'all') p.set('programId', program.value)
  if (session.value !== 'all') p.set('sessionId', session.value)
  if (range.value.from) p.set('from', range.value.from)
  if (range.value.to) p.set('to', range.value.to)
  return p.toString()
})
const exportHref = computed(() => `/api/admin/finance/reports/export?${query.value}`)

async function load() {
  loading.value = true
  try {
    report.value = await api.get<Report>(`/admin/finance/reports?${query.value}`)
    loadError.value = null
  } catch (e) {
    loadError.value = e instanceof ApiError ? e.message : "Couldn't load the report."
  } finally {
    loading.value = false
  }
}
onMounted(load)
watch(query, load)

// Narrowing the ministry clears a program outside it; narrowing the program clears a session outside it.
const options = computed(() => report.value?.options)
const programs = computed(
  () =>
    options.value?.programs.filter((p) => ministry.value === 'all' || String(p.ministryId) === ministry.value) ?? [],
)
const sessions = computed(
  () => options.value?.sessions.filter((s) => program.value === 'all' || String(s.programId) === program.value) ?? [],
)
watch(ministry, () => {
  if (!programs.value.some((p) => String(p.id) === program.value)) program.value = 'all'
})
watch(program, () => {
  if (!sessions.value.some((s) => String(s.id) === session.value)) session.value = 'all'
})

const attendancePct = computed(() => {
  const a = report.value?.attendance
  return a && a.registered ? Math.round((a.attended * 100) / a.registered) : 0
})
const topShare = (items: { label: string; count: number }[], total: number) =>
  items.map((g) => `${Math.round((g.count * 100) / (total || 1))}% ${g.label}`).join(' · ')
const maxPeriod = computed(() =>
  Math.max(1, ...(report.value?.registrationsByPeriod.periods.map((p) => p.count) ?? [])),
)
const maxRevenue = computed(() => Math.max(1, ...(report.value?.revenueByProgram.map((p) => p.amountCents) ?? [])))

const columns: ColumnDef<ReportRow>[] = [
  { id: 'program', header: 'Program / session' },
  {
    accessorKey: 'registrations',
    header: 'Registrations',
    meta: { class: 'text-right', cellClass: 'text-right tabular-nums' },
  },
  {
    accessorKey: 'attended',
    header: 'Attended',
    meta: { class: 'hidden md:table-cell text-right', cellClass: 'text-right tabular-nums' },
  },
  { id: 'revenue', header: 'Settled revenue', meta: { class: 'text-right', cellClass: 'text-right tabular-nums' } },
  {
    id: 'contracted',
    header: 'Contracted tuition',
    meta: { class: 'hidden lg:table-cell text-right', cellClass: 'text-right tabular-nums' },
  },
]
</script>

<template>
  <div class="mx-auto max-w-6xl space-y-6">
    <div class="flex flex-col gap-3 sm:flex-row sm:items-end sm:justify-between">
      <div>
        <h1 class="text-2xl font-semibold tracking-tight">Reports</h1>
        <p class="text-muted-foreground">Registration, revenue and attendance across programs and sessions.</p>
      </div>
      <Button as-child variant="outline">
        <a :href="exportHref" download><Download />Export report</a>
      </Button>
    </div>

    <Card class="py-4">
      <CardContent class="grid gap-3 px-4 sm:grid-cols-2 lg:grid-cols-4">
        <div class="space-y-1">
          <Label>Ministry</Label>
          <Select v-model="ministry">
            <SelectTrigger class="w-full" aria-label="Ministry"><SelectValue /></SelectTrigger>
            <SelectContent>
              <SelectItem value="all">All ministries</SelectItem>
              <SelectItem v-for="m in options?.ministries ?? []" :key="m.id" :value="String(m.id)">{{
                m.name
              }}</SelectItem>
            </SelectContent>
          </Select>
        </div>
        <div class="space-y-1">
          <Label>Program</Label>
          <Select v-model="program">
            <SelectTrigger class="w-full" aria-label="Program"><SelectValue /></SelectTrigger>
            <SelectContent>
              <SelectItem value="all">All programs</SelectItem>
              <SelectItem v-for="p in programs" :key="p.id" :value="String(p.id)">{{ p.name }}</SelectItem>
            </SelectContent>
          </Select>
        </div>
        <div class="space-y-1">
          <Label>Session</Label>
          <Select v-model="session">
            <SelectTrigger class="w-full" aria-label="Session"><SelectValue /></SelectTrigger>
            <SelectContent>
              <SelectItem value="all">All sessions</SelectItem>
              <SelectItem v-for="s in sessions" :key="s.id" :value="String(s.id)">{{ s.name }}</SelectItem>
            </SelectContent>
          </Select>
        </div>
        <div class="space-y-1">
          <Label>Date range</Label>
          <Select v-model="preset">
            <SelectTrigger class="w-full" aria-label="Date range"><SelectValue /></SelectTrigger>
            <SelectContent>
              <SelectItem value="30">Last 30 days</SelectItem>
              <SelectItem value="90">Last 90 days</SelectItem>
              <SelectItem value="ytd">Year to date</SelectItem>
              <SelectItem value="custom">Custom range</SelectItem>
            </SelectContent>
          </Select>
        </div>
        <div v-if="preset === 'custom'" class="grid grid-cols-2 gap-3 sm:col-span-2">
          <div class="space-y-1">
            <Label for="from">From</Label><Input id="from" v-model="customFrom" type="date" />
          </div>
          <div class="space-y-1"><Label for="to">To</Label><Input id="to" v-model="customTo" type="date" /></div>
        </div>
      </CardContent>
    </Card>

    <Alert v-if="loadError" variant="destructive"
      ><AlertDescription>{{ loadError }}</AlertDescription></Alert
    >

    <template v-if="report">
      <p class="text-sm text-muted-foreground">
        {{ report.scopeLabel }} · {{ date(report.from) }} to {{ date(report.to) }}
      </p>
      <div class="grid gap-4 sm:grid-cols-2 lg:grid-cols-4" :class="{ 'opacity-60': loading }">
        <StatTile label="Registrations" :value="report.registrations.toLocaleString()" sub="Confirmed campers in range">
          <template #icon><Users /></template>
        </StatTile>
        <StatTile label="Settled revenue" :value="money(report.settledRevenue.amountCents)">
          <template #icon><DollarSign /></template>
          <Badge
            v-if="report.settledRevenue.certified"
            variant="outline"
            class="mt-1 gap-1 border-emerald-600 text-emerald-700"
            data-testid="certified"
          >
            <BadgeCheck class="size-3" />Certified metric
          </Badge>
          <p class="mt-1 text-xs text-muted-foreground" data-testid="tie-out">
            Ties to {{ report.settledRevenue.batches }} Fiserv settlement
            {{ report.settledRevenue.batches === 1 ? 'batch' : 'batches' }}.
            <RouterLink to="/admin/finance/reconciliation" class="underline underline-offset-4"
              >Reconciliation</RouterLink
            >
          </p>
        </StatTile>
        <StatTile
          label="Attendance"
          :value="report.attendance.registered ? `${attendancePct}%` : '—'"
          :sub="
            report.attendance.registered
              ? `${report.attendance.attended} of ${report.attendance.registered} registered`
              : 'No sessions ended in this range'
          "
        >
          <template #icon><CalendarCheck /></template>
          <Progress v-if="report.attendance.registered" :model-value="attendancePct" class="mt-2 h-1.5" />
        </StatTile>
        <StatTile
          label="Demographics"
          :value="`${report.demographics.total} campers`"
          :sub="topShare(report.demographics.gender, report.demographics.total)"
        >
          <template #icon><UsersRound /></template>
          <p class="mt-1 text-xs text-muted-foreground">
            {{ topShare(report.demographics.grades, report.demographics.total) }}
          </p>
        </StatTile>
      </div>

      <div class="grid gap-4 lg:grid-cols-2">
        <Card>
          <CardHeader>
            <CardTitle>Registrations by {{ report.registrationsByPeriod.unit }}</CardTitle>
            <CardDescription>Campers registered, by date the order was placed.</CardDescription>
          </CardHeader>
          <CardContent>
            <div
              class="flex h-40 items-end gap-1"
              role="img"
              :aria-label="`Registrations by ${report.registrationsByPeriod.unit}`"
            >
              <div
                v-for="p in report.registrationsByPeriod.periods"
                :key="p.start"
                class="flex h-full min-w-0 flex-1 flex-col justify-end"
                :title="`${p.label}: ${p.count}`"
              >
                <span v-if="p.count" class="text-center text-[10px] text-muted-foreground tabular-nums">{{
                  p.count
                }}</span>
                <div class="min-h-px rounded-t bg-primary/80" :style="{ height: `${(p.count * 85) / maxPeriod}%` }" />
              </div>
            </div>
            <div class="mt-1 flex justify-between text-xs text-muted-foreground">
              <span>{{ report.registrationsByPeriod.periods[0]?.label }}</span>
              <span>{{ report.registrationsByPeriod.periods.at(-1)?.label }}</span>
            </div>
          </CardContent>
        </Card>
        <Card>
          <CardHeader>
            <CardTitle>Settled revenue by program</CardTitle>
            <CardDescription>Payments settled by Fiserv, net of refunds.</CardDescription>
          </CardHeader>
          <CardContent class="space-y-2">
            <div v-for="p in report.revenueByProgram" :key="p.program" class="space-y-1 text-sm">
              <div class="flex justify-between gap-2">
                <span class="truncate">{{ p.program }}</span
                ><span class="tabular-nums">{{ money(p.amountCents) }}</span>
              </div>
              <div class="h-2 rounded bg-muted">
                <div
                  class="h-2 rounded bg-primary/80"
                  :style="{ width: `${(Math.max(0, p.amountCents) * 100) / maxRevenue}%` }"
                />
              </div>
            </div>
            <p v-if="!report.revenueByProgram.length" class="text-sm text-muted-foreground">
              No settled revenue in this range.
            </p>
          </CardContent>
        </Card>
      </div>

      <DataTable
        :columns="columns"
        :data="report.rows"
        :loading="loading"
        empty-text="No registrations match these filters."
      >
        <template #cell-program="{ row: r }">
          <div class="font-medium">{{ r.program }}</div>
          <div class="text-xs text-muted-foreground">{{ r.session }}</div>
        </template>
        <template #cell-revenue="{ row: r }">{{ money(r.settledRevenueCents) }}</template>
        <template #cell-contracted="{ row: r }">{{ money(r.contractedCents) }}</template>
      </DataTable>

      <Alert>
        <Info />
        <AlertDescription>
          <p>
            <strong>Settled revenue</strong> is money Fiserv has deposited, summed from settlement lines in the date
            range. It matches Reconciliation to the cent. <strong>Contracted tuition</strong> is price less discounts
            and scholarships, whether or not it has been paid yet.
            <template v-if="report.settledRevenue.unattributedCents">
              {{ money(report.settledRevenue.unattributedCents) }} settled but not yet matched to a registration is
              shown as Unattributed.
            </template>
          </p>
        </AlertDescription>
      </Alert>
    </template>
    <div v-else-if="!loadError" class="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
      <Skeleton v-for="i in 4" :key="i" class="h-28" />
    </div>
  </div>
</template>

<script setup lang="ts">
import { AlertTriangle, CircleAlert, Clock, DollarSign, Search } from '@lucide/vue'
import type { ColumnDef } from '@tanstack/vue-table'
import { computed, onMounted, ref } from 'vue'
import { toast } from 'vue-sonner'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { DataTable } from '@/components/ui/data-table'
import { Input } from '@/components/ui/input'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Sheet, SheetContent, SheetDescription, SheetFooter, SheetHeader, SheetTitle } from '@/components/ui/sheet'
import { Skeleton } from '@/components/ui/skeleton'
import { api, ApiError } from '@/lib/api'
import { date, dateTime, money } from '@/lib/format'
import StatTile from './StatTile.vue'
import StatusBadge from './StatusBadge.vue'
import type { PlanException, PlanExceptionDetail, PlanExceptionList, Stage } from './types'

// FN3 · Failed plan installments, their retry schedule and grace period (FR-48).
const data = ref<PlanExceptionList | null>(null)
const loading = ref(false)
const loadError = ref<string | null>(null)
const program = ref<string>('all')
const stage = ref<'all' | Stage>('all')
const query = ref('')

async function load() {
  loading.value = true
  try {
    data.value = await api.get<PlanExceptionList>('/admin/finance/plan-exceptions')
    loadError.value = null
  } catch (e) {
    loadError.value = e instanceof ApiError ? e.message : "Couldn't load failed installments."
  } finally {
    loading.value = false
  }
}
onMounted(load)

const rows = computed(() => {
  const q = query.value.trim().toLowerCase()
  return (data.value?.rows ?? []).filter(
    (r) =>
      (program.value === 'all' || r.program === program.value) &&
      (stage.value === 'all' || r.stage === stage.value) &&
      (!q || r.family.toLowerCase().includes(q) || r.confirmationCode.toLowerCase().includes(q)),
  )
})
const programs = computed(() => [...new Set((data.value?.rows ?? []).map((r) => r.program))].toSorted())

const columns: ColumnDef<PlanException>[] = [
  { id: 'family', header: 'Family' },
  { accessorKey: 'program', header: 'Registration', meta: { class: 'hidden md:table-cell' } },
  { id: 'amount', header: 'Installment', meta: { cellClass: 'tabular-nums' } },
  { id: 'failedOn', header: 'Failed', meta: { class: 'hidden sm:table-cell', cellClass: 'whitespace-nowrap' } },
  { id: 'nextRetry', header: 'Next retry', meta: { class: 'hidden lg:table-cell', cellClass: 'whitespace-nowrap' } },
  { id: 'grace', header: 'Grace period ends', meta: { class: 'hidden lg:table-cell', cellClass: 'whitespace-nowrap' } },
  {
    accessorKey: 'daysToPolicyAction',
    header: 'Days to policy action',
    meta: { class: 'hidden md:table-cell', cellClass: 'tabular-nums' },
  },
  { id: 'status', header: 'Status' },
]

// ── Detail sheet ──
const selected = ref<PlanException | null>(null)
const detail = ref<PlanExceptionDetail | null>(null)
const busy = ref<'retry' | 'contact' | null>(null)
const actionError = ref<string | null>(null)

async function open(row: PlanException) {
  selected.value = row
  detail.value = null
  actionError.value = null
  try {
    detail.value = await api.get<PlanExceptionDetail>(`/admin/finance/plan-exceptions/${row.installmentId}`)
  } catch (e) {
    actionError.value = e instanceof ApiError ? e.message : "Couldn't load this installment."
  }
}
async function reopen(row: PlanException) {
  await load()
  await open(data.value?.rows.find((r) => r.installmentId === row.installmentId) ?? row)
}
async function retry() {
  const row = selected.value
  if (!row) return
  busy.value = 'retry'
  actionError.value = null
  try {
    const res = await api.post<{ outcome: string; message: string }>(
      `/admin/finance/plan-exceptions/${row.installmentId}/retry`,
      { idempotencyKey: crypto.randomUUID() },
    )
    if (res.outcome === 'Declined') {
      toast.error(`${row.family} family: card declined again.`)
      await reopen(row)
      actionError.value = res.message
    } else {
      toast.success(`${row.family} family: ${res.message}`)
      selected.value = null
      await load()
    }
  } catch (e) {
    actionError.value = e instanceof ApiError ? e.message : "That didn't go through. Try again."
  } finally {
    busy.value = null
  }
}
async function contact() {
  const row = selected.value
  if (!row) return
  busy.value = 'contact'
  actionError.value = null
  try {
    const res = await api.post<{ message: string }>(`/admin/finance/plan-exceptions/${row.installmentId}/contact`)
    toast.success(res.message, { description: 'HubSpot sends it within a few minutes.' })
    await reopen(row)
  } catch (e) {
    actionError.value = e instanceof ApiError ? e.message : "That didn't go through. Try again."
  } finally {
    busy.value = null
  }
}
const installmentWord: Record<string, string> = { Failed: 'failed', Paid: 'paid' }
function installmentLabel(i: { sequence: number; status: string }) {
  return installmentWord[i.status] ?? 'upcoming'
}
</script>

<template>
  <div class="mx-auto max-w-6xl space-y-6">
    <div>
      <h1 class="text-2xl font-semibold tracking-tight">Payment plan exceptions</h1>
      <p class="text-muted-foreground">
        Installments that failed. Fiserv retries automatically 3 and 7 days after a failure; the grace period ends 7
        days after it, when program policy applies.
      </p>
    </div>

    <Alert v-if="loadError" variant="destructive"
      ><AlertDescription>{{ loadError }}</AlertDescription></Alert
    >

    <div v-if="data" class="grid gap-4 sm:grid-cols-3">
      <StatTile label="Installment failed" :value="data.counts.failed" sub="Need follow-up">
        <template #icon><CircleAlert class="text-red-600" /></template>
      </StatTile>
      <StatTile
        label="Outstanding"
        :value="money(data.counts.outstandingCents)"
        :sub="`Across ${data.counts.failed} failed installments`"
      >
        <template #icon><DollarSign /></template>
      </StatTile>
      <StatTile label="Within retry window" :value="data.counts.retryScheduled" sub="Scheduled to retry automatically">
        <template #icon><Clock /></template>
      </StatTile>
    </div>
    <div v-else-if="!loadError" class="grid gap-4 sm:grid-cols-3">
      <Skeleton v-for="i in 3" :key="i" class="h-24" />
    </div>

    <div class="flex flex-col gap-2 sm:flex-row">
      <div class="relative sm:w-64">
        <Search class="absolute top-2.5 left-2.5 size-4 text-muted-foreground" />
        <Input v-model="query" class="pl-8" placeholder="Search families" aria-label="Search families" />
      </div>
      <Select v-model="program">
        <SelectTrigger class="w-full sm:w-48" aria-label="Program"><SelectValue /></SelectTrigger>
        <SelectContent>
          <SelectItem value="all">All programs</SelectItem>
          <SelectItem v-for="p in programs" :key="p" :value="p">{{ p }}</SelectItem>
        </SelectContent>
      </Select>
      <Select v-model="stage">
        <SelectTrigger class="w-full sm:w-48" aria-label="Stage"><SelectValue /></SelectTrigger>
        <SelectContent>
          <SelectItem value="all">All stages</SelectItem>
          <SelectItem value="Retry scheduled">Retry scheduled</SelectItem>
          <SelectItem value="In grace period">In grace period</SelectItem>
          <SelectItem value="Needs attention">Needs attention</SelectItem>
        </SelectContent>
      </Select>
    </div>

    <DataTable
      :columns="columns"
      :data="data ? rows : null"
      :loading="loading"
      :get-row-id="(r) => String(r.installmentId)"
      :on-row-click="open"
      empty-text="No failed installments. Every plan is on track."
    >
      <template #cell-family="{ row: r }">
        <div class="font-medium">{{ r.family }} family</div>
        <div class="text-xs text-muted-foreground md:hidden">{{ r.program }}</div>
      </template>
      <template #cell-program="{ row: r }">
        <div>{{ r.program }}</div>
        <div class="font-mono text-xs text-muted-foreground">{{ r.confirmationCode }}</div>
      </template>
      <template #cell-amount="{ row: r }">{{ money(r.amountCents) }}</template>
      <template #cell-failedOn="{ row: r }">{{ date(r.failedOn) }}</template>
      <template #cell-nextRetry="{ row: r }">{{ r.nextRetryOn ? date(r.nextRetryOn) : 'None left' }}</template>
      <template #cell-grace="{ row: r }">{{ date(r.graceEndsOn) }}</template>
      <template #cell-status="{ row: r }">
        <div class="flex flex-col items-start gap-1">
          <StatusBadge :status="r.status" />
          <span class="text-xs text-muted-foreground">{{ r.stage }}</span>
        </div>
      </template>
    </DataTable>

    <Sheet
      :open="!!selected"
      @update:open="
        (v) => {
          if (!v) selected = null
        }
      "
    >
      <SheetContent v-if="selected" class="w-full overflow-y-auto sm:max-w-md">
        <SheetHeader>
          <SheetTitle>{{ selected.family }} family</SheetTitle>
          <SheetDescription
            >{{ selected.program }} · {{ selected.session }} · {{ selected.confirmationCode }}</SheetDescription
          >
        </SheetHeader>
        <div class="space-y-5 px-4 text-sm">
          <Alert
            :class="
              selected.daysToPolicyAction === 0
                ? 'border-red-300 bg-red-50 text-red-900'
                : 'border-amber-300 bg-amber-50 text-amber-900'
            "
          >
            <AlertTriangle />
            <AlertTitle v-if="selected.daysToPolicyAction === 0"
              >Grace period ended {{ date(selected.graceEndsOn) }}</AlertTitle
            >
            <AlertTitle v-else
              >Grace period ends in {{ selected.daysToPolicyAction }}
              {{ selected.daysToPolicyAction === 1 ? 'day' : 'days' }}</AlertTitle
            >
            <AlertDescription :class="selected.daysToPolicyAction === 0 ? 'text-red-900' : 'text-amber-900'">
              {{
                selected.daysToPolicyAction === 0
                  ? 'Program policy applies now. Contact the family.'
                  : `Take action before ${date(selected.graceEndsOn)}.`
              }}
            </AlertDescription>
          </Alert>

          <Skeleton v-if="!detail" class="h-40" />
          <template v-else>
            <section class="rounded-md border p-3">
              <p class="mb-2 font-medium">Payment plan</p>
              <dl class="space-y-1">
                <div class="flex justify-between">
                  <dt>Deposit (paid)</dt>
                  <dd class="tabular-nums">{{ money(detail.plan.depositCents) }}</dd>
                </div>
                <div v-for="i in detail.plan.installments" :key="i.id" class="flex justify-between">
                  <dt :class="i.status === 'Failed' && 'text-red-700'">
                    Installment {{ i.sequence }} ({{ installmentLabel(i) }}) · {{ date(i.dueDate) }}
                  </dt>
                  <dd class="tabular-nums">{{ money(i.amountCents) }}</dd>
                </div>
                <div class="flex justify-between border-t pt-1 font-medium">
                  <dt>Total</dt>
                  <dd class="tabular-nums">{{ money(detail.plan.totalCents) }}</dd>
                </div>
              </dl>
            </section>

            <section class="rounded-md border p-3">
              <p class="mb-2 font-medium">Selected installment</p>
              <dl class="grid grid-cols-[9rem_1fr] gap-y-1">
                <dt class="text-muted-foreground">Amount</dt>
                <dd class="tabular-nums">{{ money(selected.amountCents) }}</dd>
                <dt class="text-muted-foreground">Failed date</dt>
                <dd>{{ date(selected.failedOn) }}</dd>
                <dt class="text-muted-foreground">Attempts</dt>
                <dd>{{ selected.attempts }} of 3</dd>
                <dt class="text-muted-foreground">Next retry</dt>
                <dd>{{ selected.nextRetryOn ? date(selected.nextRetryOn) : 'None left' }}</dd>
                <dt class="text-muted-foreground">Grace period ends</dt>
                <dd>{{ date(selected.graceEndsOn) }}</dd>
                <dt class="text-muted-foreground">Days to policy action</dt>
                <dd>{{ selected.daysToPolicyAction }}</dd>
                <dt class="text-muted-foreground">Status</dt>
                <dd class="flex flex-wrap gap-1">
                  <StatusBadge :status="selected.status" /><StatusBadge :status="selected.stage" />
                </dd>
                <dt class="text-muted-foreground">Decline reason</dt>
                <dd>{{ selected.declineReason }}</dd>
                <dt class="text-muted-foreground">Card on file</dt>
                <dd>{{ detail.card ? `${detail.card.brand} ending ${detail.card.last4}` : 'None' }}</dd>
                <dt class="text-muted-foreground">Contact</dt>
                <dd class="break-all">{{ detail.contact.name }} · {{ detail.contact.email }}</dd>
              </dl>
            </section>

            <section class="rounded-md border p-3">
              <p class="mb-2 font-medium">Event timeline</p>
              <ol class="space-y-3 border-l pl-4">
                <li v-for="(e, i) in detail.timeline" :key="i" class="relative">
                  <span
                    class="absolute top-1.5 -left-[1.3rem] size-2 rounded-full"
                    :class="
                      e.title.includes('failed')
                        ? 'bg-red-500'
                        : e.title === 'Retry scheduled'
                          ? 'bg-muted-foreground/40'
                          : 'bg-foreground'
                    "
                  />
                  <p class="text-xs text-muted-foreground">{{ dateTime(e.at) }} · {{ e.actor }}</p>
                  <p class="font-medium">{{ e.title }}</p>
                  <p class="text-muted-foreground">{{ e.detail }}</p>
                </li>
              </ol>
            </section>
          </template>
          <p v-if="actionError" class="text-destructive" role="alert">{{ actionError }}</p>
        </div>
        <SheetFooter class="sticky bottom-0 border-t bg-background flex-row gap-2">
          <Button variant="outline" class="flex-1" :disabled="!!busy" @click="contact">{{
            busy === 'contact' ? 'Sending…' : 'Contact family'
          }}</Button>
          <Button class="flex-1" :disabled="!!busy || !detail?.card" @click="retry">{{
            busy === 'retry' ? 'Charging…' : 'Retry now'
          }}</Button>
        </SheetFooter>
      </SheetContent>
    </Sheet>
  </div>
</template>

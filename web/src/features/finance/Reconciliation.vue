<script setup lang="ts">
import { AlertTriangle, CircleCheck, Search } from '@lucide/vue'
import type { ColumnDef } from '@tanstack/vue-table'
import { computed, onMounted, ref, watch } from 'vue'
import { RouterLink } from 'vue-router'
import { toast } from 'vue-sonner'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { DataTable } from '@/components/ui/data-table'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { RadioGroup, RadioGroupItem } from '@/components/ui/radio-group'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Sheet, SheetContent, SheetDescription, SheetFooter, SheetHeader, SheetTitle } from '@/components/ui/sheet'
import { Skeleton } from '@/components/ui/skeleton'
import { Tabs, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { Textarea } from '@/components/ui/textarea'
import { api, ApiError } from '@/lib/api'
import { date, dateRange, dateTime, money } from '@/lib/format'
import StatusBadge from './StatusBadge.vue'
import type { Candidate, SettlementDetail, SettlementLine, SettlementList } from './types'

// FN2 · A Fiserv settlement batch against the platform's payments (FR-71).
const list = ref<SettlementList | null>(null)
const batchId = ref<number | null>(null)
const batch = ref<SettlementDetail | null>(null)
const loading = ref(false)
const loadError = ref<string | null>(null)

async function loadList() {
  try {
    list.value = await api.get<SettlementList>('/admin/finance/settlements')
    batchId.value ??= list.value.batches[0]?.id ?? null
    loadError.value = null
  } catch (e) {
    loadError.value = e instanceof ApiError ? e.message : "Couldn't load settlement batches."
  }
}
async function loadBatch() {
  if (batchId.value == null) return
  loading.value = true
  try {
    batch.value = await api.get<SettlementDetail>(`/admin/finance/settlements/${batchId.value}`)
    loadError.value = null
  } catch (e) {
    loadError.value = e instanceof ApiError ? e.message : "Couldn't load this batch."
  } finally {
    loading.value = false
  }
}
onMounted(async () => {
  await loadList()
  await loadBatch()
})
watch(batchId, (_, old) => {
  if (old != null) loadBatch()
})

type View = 'all' | 'unmatched' | 'fees' | 'journal'
const view = ref<View>('all')
const lines = computed(() => {
  const all = batch.value?.lines ?? []
  if (view.value === 'unmatched') return all.filter((l) => l.status === 'Unmatched')
  if (view.value === 'fees') return all.filter((l) => l.kind === 'Fee')
  return all.filter((l) => l.kind !== 'Fee')
})
const feeCount = computed(() => batch.value?.lines.filter((l) => l.kind === 'Fee').length ?? 0)

const columns: ColumnDef<SettlementLine>[] = [
  { id: 'date', header: 'Date', meta: { cellClass: 'whitespace-nowrap' } },
  {
    accessorKey: 'processorRef',
    header: 'Processor reference',
    meta: { class: 'hidden md:table-cell', cellClass: 'font-mono text-xs' },
  },
  { id: 'registration', header: 'Registration' },
  { id: 'payer', header: 'Cardholder', meta: { class: 'hidden lg:table-cell' } },
  { id: 'amount', header: 'Gross amount', meta: { class: 'text-right', cellClass: 'text-right tabular-nums' } },
  { id: 'status', header: 'Status' },
]

// ── Resolve sheet ──
const selected = ref<SettlementLine | null>(null)
const candidates = ref<Candidate[] | null>(null)
const choice = ref<string>('')
const search = ref('')
const note = ref('')
const error = ref<string | null>(null)
const busy = ref(false)

async function loadCandidates(q = '') {
  if (!selected.value) return
  candidates.value = null
  try {
    const res = await api.get<{ candidates: Candidate[] }>(
      `/admin/finance/settlement-lines/${selected.value.id}/candidates${q ? `?q=${encodeURIComponent(q)}` : ''}`,
    )
    candidates.value = res.candidates
    const best = res.candidates.find((c) => c.canTake && c.nameMatches)
    choice.value = best ? best.confirmationCode : ''
  } catch (e) {
    candidates.value = []
    error.value = e instanceof ApiError ? e.message : "Couldn't look for matching registrations."
  }
}
function open(line: SettlementLine) {
  selected.value = line
  note.value = ''
  search.value = ''
  error.value = null
  choice.value = ''
  candidates.value = null
  if (line.status === 'Unmatched') loadCandidates()
}
const matching = computed(() => choice.value !== '' && choice.value !== 'adjustment')

async function resolve() {
  const line = selected.value
  if (!line) return
  if (!choice.value) {
    error.value = 'Choose a registration, or mark it as an adjustment.'
    return
  }
  if (!note.value.trim()) {
    error.value = 'Add an audit note saying how you know.'
    return
  }
  busy.value = true
  error.value = null
  try {
    const res = await api.post<{ journal: string | null }>(`/admin/finance/settlement-lines/${line.id}/resolve`, {
      resolution: matching.value ? 'MatchedToRegistration' : 'Adjustment',
      code: matching.value ? choice.value : null,
      note: note.value.trim(),
    })
    toast.success(
      matching.value
        ? `${money(line.amountCents)} matched to ${choice.value}. The family's balance is updated.`
        : `${money(line.amountCents)} posted to unapplied receipts.`,
      {
        description: res.journal
          ? `Every line is resolved. Journal ${res.journal} is queued for Oracle Fusion.`
          : undefined,
      },
    )
    selected.value = null
    await Promise.all([loadBatch(), loadList()])
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : "That didn't go through. Try again."
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <div class="mx-auto max-w-6xl space-y-6">
    <div>
      <h1 class="text-2xl font-semibold tracking-tight">Payment reconciliation</h1>
      <p class="text-muted-foreground">
        Compare Fiserv's settlement file with the payments WinShape recorded, and resolve what doesn't match before the
        journal goes to Oracle Fusion.
      </p>
    </div>

    <Alert v-if="loadError" variant="destructive"
      ><AlertDescription>{{ loadError }}</AlertDescription></Alert
    >

    <div class="flex flex-wrap items-center gap-3">
      <Select v-if="list" v-model="batchId">
        <SelectTrigger class="w-full sm:w-72" aria-label="Settlement batch"><SelectValue /></SelectTrigger>
        <SelectContent>
          <SelectItem v-for="b in list.batches" :key="b.id" :value="b.id">
            Fiserv batch {{ b.reference }}<template v-if="b.unmatched"> · {{ b.unmatched }} unmatched</template>
          </SelectItem>
        </SelectContent>
      </Select>
      <Skeleton v-else class="h-9 w-72" />
      <template v-if="batch">
        <span class="text-sm text-muted-foreground"
          >Settled {{ date(batch.settledOn) }} · file received {{ dateTime(batch.receivedAt) }}</span
        >
      </template>
    </div>

    <div v-if="batch" class="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
      <Card class="gap-3 py-4">
        <CardHeader class="px-4"><CardTitle class="text-sm font-medium">Processor (Fiserv)</CardTitle></CardHeader>
        <CardContent class="space-y-1 px-4 text-sm">
          <div class="flex justify-between">
            <span class="text-muted-foreground">Gross captured</span
            ><span class="tabular-nums">{{ money(batch.processor.grossCents) }}</span>
          </div>
          <div class="flex justify-between">
            <span class="text-muted-foreground">Processor fees</span
            ><span class="tabular-nums">{{ money(batch.processor.feeCents) }}</span>
          </div>
          <div class="flex justify-between border-t pt-1 font-medium">
            <span>Net settled</span
            ><span class="tabular-nums" data-testid="net-settled">{{ money(batch.processor.netCents) }}</span>
          </div>
        </CardContent>
      </Card>
      <Card class="gap-3 py-4">
        <CardHeader class="px-4"
          ><CardTitle class="text-sm font-medium">Platform payments (WinShape)</CardTitle></CardHeader
        >
        <CardContent class="space-y-1 px-4 text-sm">
          <div class="flex justify-between">
            <span class="text-muted-foreground">Gross payments</span
            ><span class="tabular-nums">{{ money(batch.platform.grossCents) }}</span>
          </div>
          <div class="flex justify-between">
            <span class="text-muted-foreground">Difference</span>
            <span
              class="tabular-nums"
              :class="batch.processor.grossCents !== batch.platform.grossCents ? 'text-amber-700' : ''"
              >{{ money(batch.processor.grossCents - batch.platform.grossCents) }}</span
            >
          </div>
          <p v-if="batch.transactions.resolved" class="text-xs text-muted-foreground">
            Adjustments posted to unapplied receipts count on the processor side only.
          </p>
        </CardContent>
      </Card>
      <Card class="gap-3 py-4">
        <CardHeader class="px-4"><CardTitle class="text-sm font-medium">Transactions</CardTitle></CardHeader>
        <CardContent class="px-4 text-sm">
          <p class="text-2xl font-semibold tabular-nums">{{ batch.transactions.total }}</p>
          <p>
            <span class="mr-1.5 inline-block size-2 rounded-full bg-emerald-500" />{{ batch.transactions.matched }}
            matched
          </p>
          <p v-if="batch.transactions.resolved">
            <span class="mr-1.5 inline-block size-2 rounded-full bg-sky-500" />{{ batch.transactions.resolved }}
            resolved
          </p>
          <p data-testid="unmatched-count">
            <span class="mr-1.5 inline-block size-2 rounded-full bg-amber-500" />{{ batch.transactions.unmatched }}
            unmatched
          </p>
        </CardContent>
      </Card>
      <Card class="gap-3 py-4">
        <CardHeader class="px-4"
          ><CardTitle class="text-sm font-medium">Journal entry (Oracle Fusion)</CardTitle></CardHeader
        >
        <CardContent class="space-y-2 px-4 text-sm" data-testid="journal-status">
          <StatusBadge :status="batch.journal.status" />
          <p class="text-muted-foreground">{{ batch.journal.detail }}</p>
          <RouterLink v-if="batch.journal.id" to="/admin/finance/journals" class="text-sm underline underline-offset-4"
            >Open {{ batch.journal.reference }}</RouterLink
          >
        </CardContent>
      </Card>
    </div>
    <div v-else-if="!loadError" class="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
      <Skeleton v-for="i in 4" :key="i" class="h-36" />
    </div>

    <Alert v-if="batch?.transactions.unmatched" class="border-amber-300 bg-amber-50 text-amber-900">
      <AlertTriangle />
      <AlertTitle
        >{{ batch.transactions.unmatched }} unmatched
        {{ batch.transactions.unmatched === 1 ? 'item needs' : 'items need' }} review</AlertTitle
      >
      <AlertDescription class="flex flex-wrap items-center justify-between gap-2 text-amber-900">
        <span
          >{{ money(batch.transactions.unmatchedCents) }} reached Fiserv with no registration on our side. Resolve them
          so the journal can be created.</span
        >
        <Button size="sm" variant="outline" class="bg-background" @click="view = 'unmatched'"
          >View unmatched only</Button
        >
      </AlertDescription>
    </Alert>
    <Alert v-else-if="batch" class="border-emerald-200 bg-emerald-50 text-emerald-900">
      <CircleCheck />
      <AlertTitle>Every line in {{ batch.reference }} is matched or resolved</AlertTitle>
    </Alert>
    <p v-if="list?.unsettled.count" class="text-sm text-muted-foreground">
      {{ list.unsettled.count }} {{ list.unsettled.count === 1 ? 'payment' : 'payments' }} ({{
        money(list.unsettled.amountCents)
      }}) captured since the last file: captured, not yet settled.
    </p>

    <Tabs v-model="view">
      <TabsList class="w-full justify-start overflow-x-auto sm:w-auto">
        <TabsTrigger value="all">Transactions ({{ batch?.transactions.total ?? '…' }})</TabsTrigger>
        <TabsTrigger value="unmatched">Unmatched ({{ batch?.transactions.unmatched ?? '…' }})</TabsTrigger>
        <TabsTrigger value="fees">Fees ({{ feeCount }})</TabsTrigger>
      </TabsList>
    </Tabs>

    <DataTable
      :columns="columns"
      :data="batch ? lines : null"
      :loading="loading"
      :get-row-id="(r) => String(r.id)"
      :on-row-click="open"
      :empty-text="view === 'unmatched' ? 'Nothing unmatched in this batch.' : 'No lines.'"
    >
      <template #cell-date="{ row: r }">{{ dateTime(r.transactedAt) }}</template>
      <template #cell-registration="{ row: r }">
        <span v-if="r.confirmationCode" class="font-mono text-xs">{{ r.confirmationCode }}</span>
        <span v-else-if="r.kind === 'Fee'" class="text-muted-foreground">{{ r.description }}</span>
        <span v-else-if="r.resolution === 'Adjustment'" class="text-muted-foreground">Unapplied receipts</span>
        <span v-else class="text-muted-foreground">—</span>
        <div v-if="r.program" class="text-xs text-muted-foreground">{{ r.program }} · {{ r.session }}</div>
      </template>
      <template #cell-payer="{ row: r }">
        {{ r.cardholderName ?? '—' }}<span v-if="r.cardLast4" class="text-muted-foreground"> ·· {{ r.cardLast4 }}</span>
      </template>
      <template #cell-amount="{ row: r }">{{ money(r.amountCents) }}</template>
      <template #cell-status="{ row: r }"><StatusBadge :status="r.kind === 'Fee' ? 'Fee' : r.status" /></template>
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
          <SheetTitle>{{ selected.status === 'Unmatched' ? 'Resolve transaction' : 'Transaction' }}</SheetTitle>
          <SheetDescription>{{ selected.description }} in {{ batch?.reference }}</SheetDescription>
        </SheetHeader>
        <div class="space-y-5 px-4 text-sm">
          <dl class="grid grid-cols-[9rem_1fr] gap-x-3 gap-y-2">
            <dt class="text-muted-foreground">Date</dt>
            <dd>{{ dateTime(selected.transactedAt) }}</dd>
            <dt class="text-muted-foreground">Processor reference</dt>
            <dd class="font-mono text-xs break-all">{{ selected.processorRef }}</dd>
            <dt class="text-muted-foreground">Gross amount</dt>
            <dd class="tabular-nums">{{ money(selected.amountCents) }}</dd>
            <template v-if="selected.cardholderName">
              <dt class="text-muted-foreground">Cardholder</dt>
              <dd>{{ selected.cardholderName }} ·· {{ selected.cardLast4 }}</dd>
            </template>
            <dt class="text-muted-foreground">Status</dt>
            <dd><StatusBadge :status="selected.kind === 'Fee' ? 'Fee' : selected.status" /></dd>
            <template v-if="selected.confirmationCode">
              <dt class="text-muted-foreground">Registration</dt>
              <dd class="font-mono text-xs">{{ selected.confirmationCode }}</dd>
            </template>
          </dl>

          <div v-if="selected.status === 'Resolved'" class="rounded-md border p-3">
            <p class="font-medium">
              {{ selected.resolution === 'Adjustment' ? 'Posted to unapplied receipts' : 'Matched to a registration' }}
              by {{ selected.resolvedBy }}
            </p>
            <p class="text-muted-foreground">{{ selected.resolvedAt ? dateTime(selected.resolvedAt) : '' }}</p>
            <p class="mt-1">{{ selected.resolutionNote }}</p>
          </div>

          <template v-if="selected.status === 'Unmatched'">
            <div>
              <p class="font-medium">Reason</p>
              <p class="text-muted-foreground">{{ selected.unmatchedReason }}</p>
            </div>

            <div class="space-y-3">
              <p class="font-medium">Candidate registration</p>
              <form class="flex gap-2" @submit.prevent="loadCandidates(search.trim())">
                <Input
                  v-model="search"
                  aria-label="Find a registration"
                  placeholder="Confirmation code or family name"
                />
                <Button type="submit" variant="outline" size="icon" aria-label="Search"><Search /></Button>
              </form>
              <Skeleton v-if="!candidates" class="h-16" />
              <RadioGroup v-else v-model="choice" class="gap-2">
                <p v-if="!candidates.length" class="text-muted-foreground">
                  No registration with this cardholder's name owes this amount. Search above, or resolve it as an
                  adjustment.
                </p>
                <p v-else class="text-muted-foreground">
                  {{ candidates.length }} possible {{ candidates.length === 1 ? 'match' : 'matches' }} by cardholder
                  name and balance owed.
                </p>
                <Label
                  v-for="c in candidates"
                  :key="c.confirmationCode"
                  :for="`c-${c.confirmationCode}`"
                  class="flex items-start gap-3 rounded-md border p-3 font-normal has-[[data-state=checked]]:border-primary"
                  :class="!c.canTake && 'opacity-60'"
                >
                  <RadioGroupItem
                    :id="`c-${c.confirmationCode}`"
                    :value="c.confirmationCode"
                    :disabled="!c.canTake"
                    class="mt-0.5"
                  />
                  <span class="min-w-0 flex-1">
                    <span class="flex justify-between gap-2 font-medium"
                      ><span class="font-mono text-xs">{{ c.confirmationCode }}</span
                      ><span>owes {{ money(c.balanceCents) }}</span></span
                    >
                    <span class="block">{{ c.payer ?? c.household }} · {{ c.campers.join(', ') }}</span>
                    <span class="block text-muted-foreground"
                      >{{ c.program }} · {{ c.session }} · {{ dateRange(c.startDate, c.endDate) }}</span
                    >
                    <span v-if="!c.canTake" class="block text-amber-800">Owes less than this payment.</span>
                  </span>
                </Label>
                <Label
                  for="c-adjustment"
                  class="flex items-start gap-3 rounded-md border p-3 font-normal has-[[data-state=checked]]:border-primary"
                >
                  <RadioGroupItem id="c-adjustment" value="adjustment" class="mt-0.5" />
                  <span>
                    <span class="block font-medium">None of these match</span>
                    <span class="block text-muted-foreground"
                      >Post it to unapplied receipts (2400) as an adjustment.</span
                    >
                  </span>
                </Label>
              </RadioGroup>
            </div>

            <div class="space-y-2">
              <Label for="audit-note">Audit note</Label>
              <Textarea
                id="audit-note"
                v-model="note"
                rows="3"
                maxlength="500"
                placeholder="How you know, e.g. confirmed with the front desk."
              />
              <p class="text-right text-xs text-muted-foreground">{{ note.length }}/500</p>
            </div>
            <p v-if="error" class="text-destructive" role="alert">{{ error }}</p>
          </template>
        </div>
        <SheetFooter v-if="selected.status === 'Unmatched'" class="gap-2">
          <Button :disabled="busy" @click="resolve">{{
            busy ? 'Resolving…' : matching ? 'Resolve and match' : 'Resolve as adjustment'
          }}</Button>
          <Button variant="outline" :disabled="busy" @click="selected = null">Cancel</Button>
        </SheetFooter>
      </SheetContent>
    </Sheet>
  </div>
</template>

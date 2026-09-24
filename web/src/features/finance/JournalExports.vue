<script setup lang="ts">
import { ChevronLeft, ChevronRight, CircleCheck, Clock, FileText, RotateCw, TriangleAlert } from '@lucide/vue'
import type { ColumnDef } from '@tanstack/vue-table'
import { computed, onMounted, ref, watch } from 'vue'
import { RouterLink } from 'vue-router'
import { toast } from 'vue-sonner'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { DataTable } from '@/components/ui/data-table'
import { Sheet, SheetContent, SheetDescription, SheetFooter, SheetHeader, SheetTitle } from '@/components/ui/sheet'
import { Skeleton } from '@/components/ui/skeleton'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { api, ApiError } from '@/lib/api'
import { date, dateTime, money } from '@/lib/format'
import StatTile from './StatTile.vue'
import StatusBadge from './StatusBadge.vue'
import type { JournalDetail, JournalList, JournalRow, JournalStatus } from './types'

// FN4 · Journal batches sent to Oracle Fusion, one per reconciled Fiserv settlement (FR-111).
const data = ref<JournalList | null>(null)
const loading = ref(false)
const loadError = ref<string | null>(null)
const filter = ref<'all' | JournalStatus>('all')

async function load() {
  loading.value = true
  try {
    data.value = await api.get<JournalList>('/admin/finance/journals')
    loadError.value = null
  } catch (e) {
    loadError.value = e instanceof ApiError ? e.message : "Couldn't load journal exports."
  } finally {
    loading.value = false
  }
}
onMounted(load)

const rows = computed(() => (data.value?.rows ?? []).filter((r) => filter.value === 'all' || r.status === filter.value))

// 20 batches a page: a year of daily batches is otherwise one very long table.
const PAGE_SIZE = 20
const page = ref(0)
watch(filter, () => (page.value = 0))
const pageCount = computed(() => Math.max(1, Math.ceil(rows.value.length / PAGE_SIZE)))
const pageRows = computed(() => rows.value.slice(page.value * PAGE_SIZE, (page.value + 1) * PAGE_SIZE))
const pageLabel = computed(() => {
  const n = rows.value.length
  if (!n) return ''
  const from = page.value * PAGE_SIZE + 1
  return `${from}–${Math.min(n, from + PAGE_SIZE - 1)} of ${n} batches`
})
const pct = (n: number) => (data.value?.counts.total ? `${Math.round((n * 100) / data.value.counts.total)}%` : '')
const waiting = computed(() => data.value?.waiting.filter((w) => w.unmatched > 0) ?? [])

const columns: ColumnDef<JournalRow>[] = [
  { id: 'date', header: 'Date', meta: { cellClass: 'whitespace-nowrap' } },
  { accessorKey: 'reference', header: 'Journal batch', meta: { cellClass: 'font-mono text-xs' } },
  { accessorKey: 'source', header: 'Source', meta: { class: 'hidden lg:table-cell' } },
  {
    accessorKey: 'entries',
    header: 'Entries',
    meta: { class: 'hidden md:table-cell text-right', cellClass: 'text-right tabular-nums' },
  },
  {
    id: 'debit',
    header: 'Debit total',
    meta: { class: 'hidden sm:table-cell text-right', cellClass: 'text-right tabular-nums' },
  },
  {
    id: 'credit',
    header: 'Credit total',
    meta: { class: 'hidden xl:table-cell text-right', cellClass: 'text-right tabular-nums' },
  },
  { id: 'status', header: 'Status' },
]

// ── Detail sheet ──
const selected = ref<JournalRow | null>(null)
const detail = ref<JournalDetail | null>(null)
const busy = ref(false)
const actionError = ref<string | null>(null)

async function open(row: JournalRow) {
  selected.value = row
  detail.value = null
  actionError.value = null
  try {
    detail.value = await api.get<JournalDetail>(`/admin/finance/journals/${row.id}`)
  } catch (e) {
    actionError.value = e instanceof ApiError ? e.message : "Couldn't load this journal."
  }
}
async function retry() {
  const row = selected.value
  if (!row) return
  busy.value = true
  actionError.value = null
  try {
    await api.post(`/admin/finance/journals/${row.id}/retry`)
    toast.success(`${row.reference} resent to Oracle Fusion.`, {
      description: 'Its status is Pending until Fusion posts it.',
    })
    await load()
    await open(data.value?.rows.find((r) => r.id === row.id) ?? row)
  } catch (e) {
    actionError.value = e instanceof ApiError ? e.message : "That didn't go through. Try again."
  } finally {
    busy.value = false
  }
}
const dot: Record<JournalStatus, string> = {
  Pending: 'border-amber-500',
  Posted: 'border-emerald-600',
  Failed: 'border-red-600',
}
</script>

<template>
  <div class="mx-auto max-w-6xl space-y-6">
    <div>
      <h1 class="text-2xl font-semibold tracking-tight">Oracle Fusion journal exports</h1>
      <p class="text-muted-foreground">
        One journal batch per Fiserv settlement, created once every line is reconciled and exported to Oracle Fusion for
        posting.
      </p>
    </div>

    <Alert v-if="loadError" variant="destructive"
      ><AlertDescription>{{ loadError }}</AlertDescription></Alert
    >

    <div v-if="data" class="grid grid-cols-2 gap-4 lg:grid-cols-4">
      <StatTile label="Total batches" :value="data.counts.total"
        ><template #icon><FileText /></template
      ></StatTile>
      <StatTile label="Posted" :value="data.counts.posted" :sub="pct(data.counts.posted)"
        ><template #icon><CircleCheck class="text-emerald-600" /></template
      ></StatTile>
      <StatTile label="Pending" :value="data.counts.pending" :sub="pct(data.counts.pending)"
        ><template #icon><Clock class="text-amber-600" /></template
      ></StatTile>
      <StatTile label="Failed" :value="data.counts.failed" :sub="pct(data.counts.failed)"
        ><template #icon><TriangleAlert class="text-red-600" /></template
      ></StatTile>
    </div>
    <div v-else-if="!loadError" class="grid grid-cols-2 gap-4 lg:grid-cols-4">
      <Skeleton v-for="i in 4" :key="i" class="h-24" />
    </div>

    <Alert v-for="w in waiting" :key="w.id">
      <Clock />
      <AlertTitle>{{ w.reference }} is waiting on reconciliation</AlertTitle>
      <AlertDescription>
        <span
          >{{ w.unmatched }} unmatched {{ w.unmatched === 1 ? 'line' : 'lines' }}. The journal is created when every
          line is resolved.</span
        >
        <RouterLink to="/admin/finance/reconciliation" class="ml-1 underline underline-offset-4"
          >Open reconciliation</RouterLink
        >
      </AlertDescription>
    </Alert>

    <Tabs v-model="filter">
      <TabsList>
        <TabsTrigger value="all">All</TabsTrigger>
        <TabsTrigger value="Failed">Failed ({{ data?.counts.failed ?? '…' }})</TabsTrigger>
        <TabsTrigger value="Pending">Pending</TabsTrigger>
        <TabsTrigger value="Posted">Posted</TabsTrigger>
      </TabsList>
    </Tabs>

    <DataTable
      :columns="columns"
      :data="data ? pageRows : null"
      :loading="loading"
      :get-row-id="(r) => String(r.id)"
      :on-row-click="open"
      empty-text="No journal batches here."
    >
      <template #cell-date="{ row: r }">{{ date(r.settledOn) }}</template>
      <template #cell-debit="{ row: r }">{{ money(r.debitCents) }}</template>
      <template #cell-credit="{ row: r }">{{ money(r.creditCents) }}</template>
      <template #cell-status="{ row: r }"><StatusBadge :status="r.status" /></template>
    </DataTable>

    <nav v-if="rows.length > PAGE_SIZE" aria-label="Journal batch pages" class="flex items-center justify-end gap-2">
      <span class="mr-2 text-sm text-muted-foreground tabular-nums" aria-live="polite">{{ pageLabel }}</span>
      <Button variant="outline" size="sm" :disabled="page === 0" @click="page--">
        <ChevronLeft aria-hidden="true" />Previous
      </Button>
      <Button variant="outline" size="sm" :disabled="page >= pageCount - 1" @click="page++">
        Next<ChevronRight aria-hidden="true" />
      </Button>
    </nav>

    <Sheet
      :open="!!selected"
      @update:open="
        (v) => {
          if (!v) selected = null
        }
      "
    >
      <SheetContent v-if="selected" class="w-full overflow-y-auto sm:max-w-lg">
        <SheetHeader>
          <SheetTitle class="flex items-center gap-2"
            ><span class="font-mono">{{ selected.reference }}</span>
            <StatusBadge :status="detail?.status ?? selected.status"
          /></SheetTitle>
          <SheetDescription
            >{{ date(selected.settledOn) }} · Fiserv settlement {{ selected.settlementReference }}</SheetDescription
          >
        </SheetHeader>
        <div class="space-y-4 px-4 text-sm">
          <Skeleton v-if="!detail" class="h-48" />
          <template v-else>
            <Alert v-if="detail.status === 'Failed'" variant="destructive">
              <TriangleAlert />
              <AlertTitle>Export failed</AlertTitle>
              <AlertDescription>{{ detail.errorDetail }}</AlertDescription>
            </Alert>
            <Tabs default-value="details">
              <TabsList>
                <TabsTrigger value="details">Details</TabsTrigger>
                <TabsTrigger value="trail">Export trail</TabsTrigger>
                <TabsTrigger value="lines">Journal lines</TabsTrigger>
              </TabsList>
              <TabsContent value="details" class="space-y-3 pt-2">
                <dl class="grid grid-cols-[9rem_1fr] gap-y-1 rounded-md border p-3">
                  <dt class="text-muted-foreground">Journal batch</dt>
                  <dd class="font-mono text-xs">{{ detail.reference }}</dd>
                  <dt class="text-muted-foreground">Source</dt>
                  <dd>Fiserv settlement ({{ detail.settlement.reference }})</dd>
                  <dt class="text-muted-foreground">Entries</dt>
                  <dd>{{ detail.entries }} settlement lines</dd>
                  <dt class="text-muted-foreground">Gross amount</dt>
                  <dd class="tabular-nums">{{ money(detail.grossCents) }}</dd>
                  <dt class="text-muted-foreground">Processing fees</dt>
                  <dd class="tabular-nums">{{ money(detail.feeCents) }}</dd>
                  <dt class="text-muted-foreground">Net amount</dt>
                  <dd class="tabular-nums">{{ money(detail.netCents) }}</dd>
                  <dt class="border-t pt-1 text-muted-foreground">Debit total</dt>
                  <dd class="border-t pt-1 tabular-nums">{{ money(detail.debitCents) }}</dd>
                  <dt class="text-muted-foreground">Credit total</dt>
                  <dd class="tabular-nums">{{ money(detail.creditCents) }}</dd>
                </dl>
                <p class="flex items-center gap-1.5" :class="detail.balanced ? 'text-emerald-700' : 'text-destructive'">
                  <CircleCheck v-if="detail.balanced" class="size-4" /><TriangleAlert v-else class="size-4" />
                  {{ detail.balanced ? 'Debits and credits are balanced.' : 'Debits and credits do not balance.' }}
                </p>
              </TabsContent>
              <TabsContent value="trail" class="pt-2">
                <ol class="space-y-3">
                  <li v-for="(e, i) in detail.events" :key="i" class="flex gap-3">
                    <span class="mt-1 size-3 shrink-0 rounded-full border-2" :class="dot[e.status]" />
                    <div>
                      <p class="font-medium">
                        {{ e.status }}
                        <span class="font-normal text-muted-foreground">· {{ dateTime(e.at) }} · {{ e.actor }}</span>
                      </p>
                      <p class="text-muted-foreground">{{ e.detail }}</p>
                    </div>
                  </li>
                </ol>
              </TabsContent>
              <TabsContent value="lines" class="pt-2">
                <div class="overflow-x-auto rounded-md border">
                  <Table>
                    <TableHeader
                      ><TableRow
                        ><TableHead>Account</TableHead><TableHead class="text-right">Debit</TableHead
                        ><TableHead class="text-right">Credit</TableHead></TableRow
                      ></TableHeader
                    >
                    <TableBody>
                      <TableRow v-for="(l, i) in detail.journalLines" :key="i">
                        <TableCell>
                          <div class="font-medium">{{ l.account }} {{ l.accountName }}</div>
                          <div class="text-xs text-muted-foreground">
                            {{ l.department ? `${l.department} · ` : '' }}{{ l.description }}
                          </div>
                        </TableCell>
                        <TableCell class="text-right tabular-nums">{{
                          l.debitCents ? money(l.debitCents) : ''
                        }}</TableCell>
                        <TableCell class="text-right tabular-nums">{{
                          l.creditCents ? money(l.creditCents) : ''
                        }}</TableCell>
                      </TableRow>
                    </TableBody>
                  </Table>
                </div>
              </TabsContent>
            </Tabs>
          </template>
          <p v-if="actionError" class="text-destructive" role="alert">{{ actionError }}</p>
        </div>
        <SheetFooter v-if="detail?.status === 'Failed'" class="sticky bottom-0 border-t bg-background">
          <Button :disabled="busy" @click="retry"><RotateCw />{{ busy ? 'Resending…' : 'Retry export' }}</Button>
        </SheetFooter>
      </SheetContent>
    </Sheet>
  </div>
</template>

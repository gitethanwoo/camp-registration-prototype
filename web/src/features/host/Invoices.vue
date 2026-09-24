<script setup lang="ts">
import { AlertTriangle } from '@lucide/vue'
import type { ColumnDef } from '@tanstack/vue-table'
import { computed, nextTick, onMounted, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { DataTable } from '@/components/ui/data-table'
import { api, ApiError } from '@/lib/api'
import { date, money } from '@/lib/format'
import InvoiceBadge from './InvoiceBadge.vue'
import InvoicePanel from './InvoicePanel.vue'
import type { InvoiceSummary } from './types'
import { useHost } from './useHost'

// H3 · Invoices & payments: WinShape's invoices to this church, line items, and Pay (FR-89).
const props = defineProps<{ id?: number }>()
const router = useRouter()
const { me } = useHost()

const invoices = ref<InvoiceSummary[] | null>(null)
const error = ref<string | null>(null)
const panel = ref<HTMLElement | null>(null)

async function load() {
  try {
    invoices.value = await api.get<InvoiceSummary[]>('/host/invoices')
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : "Couldn't load your invoices."
  }
}
onMounted(load)

// With no invoice in the URL, open the one that needs attention: the earliest with a balance.
const selectedId = computed(() => {
  if (props.id) return props.id
  const list = invoices.value ?? []
  const due = list.filter((i) => i.balanceCents > 0).toSorted((a, b) => a.dueDate.localeCompare(b.dueDate))
  return (due[0] ?? list[0])?.id ?? null
})

function open(row: InvoiceSummary) {
  router.push(`/host/invoices/${row.id}`)
}
watch(
  () => props.id,
  async (id) => {
    // On a phone the detail sits under the list; bring it into view when an invoice is picked.
    if (!id || window.matchMedia('(min-width: 1024px)').matches) return
    await nextTick()
    panel.value?.scrollIntoView({ behavior: 'smooth', block: 'start' })
  },
)

const columns: ColumnDef<InvoiceSummary>[] = [
  { id: 'number', header: 'Invoice #' },
  { id: 'event', header: 'Event', meta: { class: 'hidden md:table-cell' } },
  {
    id: 'amount',
    header: 'Amount',
    meta: { class: 'text-right', cellClass: 'text-right tabular-nums' },
  },
  {
    id: 'dueDate',
    header: 'Due date',
    meta: { class: 'hidden sm:table-cell' },
  },
  { id: 'status', header: 'Status' },
]
</script>

<template>
  <div class="mx-auto max-w-7xl space-y-6">
    <div>
      <h1 class="text-2xl font-semibold tracking-tight md:text-3xl">Invoices &amp; payments</h1>
      <p class="mt-1 text-muted-foreground">
        <template v-if="me">{{ me.organization }} · {{ me.city }}</template>
        <template v-else>View and pay your invoices from WinShape Camps.</template>
      </p>
    </div>

    <Alert v-if="error" variant="destructive">
      <AlertTriangle />
      <AlertDescription>{{ error }}</AlertDescription>
    </Alert>

    <div class="grid items-start gap-6 lg:grid-cols-[minmax(0,1fr)_28rem] xl:grid-cols-[minmax(0,1fr)_32rem]">
      <Card>
        <CardHeader>
          <CardTitle>Invoices</CardTitle>
          <CardDescription v-if="invoices"
            >{{ invoices.length }} {{ invoices.length === 1 ? 'invoice' : 'invoices' }}</CardDescription
          >
        </CardHeader>
        <CardContent>
          <DataTable
            :columns="columns"
            :data="invoices"
            :get-row-id="(i) => String(i.id)"
            :on-row-click="open"
            empty-text="No invoices yet."
          >
            <template #cell-number="{ row }">
              <span :class="['font-medium', row.id === selectedId ? 'underline underline-offset-4' : '']">{{
                row.number
              }}</span>
              <span class="block text-xs text-muted-foreground md:hidden"
                >{{ row.description }} · {{ row.period }}</span
              >
            </template>
            <template #cell-event="{ row }">
              {{ row.description }}<span class="block text-xs text-muted-foreground">{{ row.period }}</span>
            </template>
            <template #cell-amount="{ row }">{{ money(row.totalCents) }}</template>
            <template #cell-dueDate="{ row }">{{ date(row.dueDate) }}</template>
            <template #cell-status="{ row }"><InvoiceBadge :status="row.status" /></template>
          </DataTable>
        </CardContent>
      </Card>

      <div ref="panel" class="min-w-0 scroll-mt-4">
        <InvoicePanel v-if="selectedId" :id="selectedId" :key="selectedId" @paid="load" />
      </div>
    </div>
  </div>
</template>

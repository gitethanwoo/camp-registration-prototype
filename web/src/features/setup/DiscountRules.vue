<script setup lang="ts">
import { Plus } from '@lucide/vue'
import type { ColumnDef } from '@tanstack/vue-table'
import { computed, onMounted, ref } from 'vue'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { DataTable } from '@/components/ui/data-table'
import { Tabs, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { date } from '@/lib/format'
import AdminOnly from './AdminOnly.vue'
import DiscountRuleSheet from './DiscountRuleSheet.vue'
import PublishBadge from './PublishBadge.vue'
import type { DiscountRuleRow, DiscountRules, RuleStatus } from './types'
import { useSetupLoad } from './useSetupLoad'

// K5 · Discount rules (FR-62). Admin rules are live when saved; host and partner codes wait in Discount approvals.
const { data, loading, forbidden, error, load } = useSetupLoad<DiscountRules>(() => '/admin/setup/discount-rules')
onMounted(load)

type Filter = 'all' | RuleStatus
const filter = ref<Filter>('all')
const rows = computed(() => (data.value?.rows ?? []).filter((r) => filter.value === 'all' || r.status === filter.value))

const columns: ColumnDef<DiscountRuleRow>[] = [
  { accessorKey: 'code', header: 'Code' },
  { accessorKey: 'description', header: 'Discount', meta: { class: 'hidden sm:table-cell' } },
  {
    accessorKey: 'scope',
    header: 'Applies to',
    meta: { class: 'hidden lg:table-cell', cellClass: 'text-muted-foreground' },
  },
  { id: 'dates', header: 'Valid dates', meta: { class: 'hidden md:table-cell' } },
  {
    id: 'usage',
    header: 'Usage',
    meta: { class: 'hidden md:table-cell text-right', cellClass: 'text-right tabular-nums' },
  },
  { accessorKey: 'status', header: 'Status' },
]

const openId = ref<number | 'new' | null>(null)
const selected = computed(() =>
  typeof openId.value === 'number' ? (data.value?.rows.find((r) => r.id === openId.value) ?? null) : null,
)
async function saved(id?: number) {
  await load()
  openId.value = id ?? null
}
</script>

<template>
  <AdminOnly v-if="forbidden" page="Discount rules" />
  <div v-else class="mx-auto max-w-6xl space-y-6">
    <div class="flex flex-wrap items-start justify-between gap-4">
      <div>
        <h1 class="text-2xl font-semibold tracking-tight">Discount rules</h1>
        <p class="text-muted-foreground">
          Codes families type at checkout. A code only works inside its dates and scope, and stops at its usage cap.
        </p>
      </div>
      <Button @click="openId = 'new'"><Plus />New rule</Button>
    </div>

    <Tabs v-model="filter">
      <TabsList class="max-w-full overflow-x-auto">
        <TabsTrigger value="all">All ({{ data?.counts.all ?? '…' }})</TabsTrigger>
        <TabsTrigger value="Active">Active ({{ data?.counts.active ?? '…' }})</TabsTrigger>
        <TabsTrigger value="Pending approval">Pending ({{ data?.counts.pending ?? '…' }})</TabsTrigger>
        <TabsTrigger value="Inactive">Inactive ({{ data?.counts.inactive ?? '…' }})</TabsTrigger>
      </TabsList>
    </Tabs>

    <Alert v-if="error" variant="destructive"
      ><AlertDescription>{{ error }}</AlertDescription></Alert
    >
    <DataTable
      v-else
      :columns="columns"
      :data="rows"
      :loading="loading && !data"
      :get-row-id="(r) => String(r.id)"
      :on-row-click="(r) => (openId = r.id)"
      empty-text="No codes here."
    >
      <template #cell-code="{ row: r }">
        <div class="font-mono font-medium">{{ r.code }}</div>
        <div class="text-sm text-muted-foreground">
          {{ r.hasRule ? r.name : r.source }}<span class="sm:hidden"> · {{ r.description }}</span>
        </div>
      </template>
      <template #cell-dates="{ row: r }">
        <span v-if="r.validFrom" class="whitespace-nowrap">{{ date(r.validFrom) }} – {{ date(r.validTo) }}</span>
        <span v-else class="text-muted-foreground">No limit</span>
      </template>
      <template #cell-usage="{ row: r }">{{ r.uses }} / {{ r.maxUses ?? '∞' }}</template>
      <template #cell-status="{ row: r }"
        ><PublishBadge :state="r.status" :label="r.status === 'Pending approval' ? 'Pending' : r.status"
      /></template>
    </DataTable>

    <DiscountRuleSheet
      :open="openId !== null"
      :rule="selected"
      :scopes="data?.scopes ?? []"
      @close="openId = null"
      @saved="saved"
    />
  </div>
</template>

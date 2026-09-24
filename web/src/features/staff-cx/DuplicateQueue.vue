<script setup lang="ts">
import type { ColumnDef } from '@tanstack/vue-table'
import { onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { DataTable } from '@/components/ui/data-table'
import { api, ApiError } from '@/lib/api'
import type { DuplicateSummary } from './types'

// C9 · Likely duplicate accounts: same person (name + date of birth) and the same phone number.
const router = useRouter()
const rows = ref<DuplicateSummary[] | null>(null)
const error = ref<string | null>(null)
onMounted(async () => {
  try {
    rows.value = await api.get<DuplicateSummary[]>('/admin/duplicates')
  } catch (e) {
    error.value =
      e instanceof ApiError && e.status === 403
        ? 'Only the Customer Experience team reviews and merges duplicate accounts.'
        : "Couldn't load duplicates."
  }
})

const columns: ColumnDef<DuplicateSummary>[] = [
  { id: 'people', header: 'Shared person', meta: { cellClass: 'font-medium' } },
  { id: 'a', header: 'Account A' },
  { id: 'b', header: 'Account B', meta: { class: 'hidden sm:table-cell' } },
  {
    accessorKey: 'reason',
    header: 'Match reason',
    meta: { class: 'hidden lg:table-cell', cellClass: 'text-muted-foreground' },
  },
  { id: 'conflicts', header: 'Conflicts' },
]
const open = (r: DuplicateSummary) => router.push(`/admin/duplicates/${r.householdA}/${r.householdB}`)
</script>

<template>
  <div class="mx-auto max-w-6xl space-y-6">
    <div>
      <h1 class="text-2xl font-semibold tracking-tight">Duplicate accounts</h1>
      <p class="text-muted-foreground">
        Accounts that share a person's name, date of birth and phone number. Open one to compare and merge.
      </p>
    </div>
    <Alert v-if="error" variant="destructive"
      ><AlertDescription>{{ error }}</AlertDescription></Alert
    >
    <DataTable
      v-else
      :columns="columns"
      :data="rows"
      :get-row-id="(r) => `${r.householdA}-${r.householdB}`"
      :on-row-click="open"
      empty-text="No likely duplicates right now."
    >
      <template #cell-people="{ row: r }">{{ r.sharedPeople.join(', ') }}</template>
      <template #cell-a="{ row: r }">
        <div>#{{ r.a.id }} · {{ r.a.name }}</div>
        <div class="text-sm text-muted-foreground">{{ r.a.email }}</div>
      </template>
      <template #cell-b="{ row: r }">
        <div>#{{ r.b.id }} · {{ r.b.name }}</div>
        <div class="text-sm text-muted-foreground">{{ r.b.email }}</div>
      </template>
      <template #cell-conflicts="{ row: r }">
        <Badge v-if="r.conflicts" variant="outline" class="border-amber-300 bg-amber-50 text-amber-900"
          >{{ r.conflicts }} to resolve</Badge
        >
        <span v-else class="text-sm text-muted-foreground">None</span>
      </template>
    </DataTable>
  </div>
</template>

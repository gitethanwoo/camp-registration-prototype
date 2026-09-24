<script setup lang="ts" generic="TData">
import type { Column } from '@tanstack/vue-table'
import { ArrowDown, ArrowUp, ChevronsUpDown } from '@lucide/vue'
import { Button } from '@/components/ui/button'

const props = defineProps<{ column: Column<TData, unknown>; title: string }>()

function toggle() {
  const s = props.column.getIsSorted()
  // Re-clicking flips direction; a new column starts in its natural direction.
  props.column.toggleSorting(s ? s === 'asc' : !!props.column.columnDef.sortDescFirst)
}
</script>

<template>
  <Button variant="ghost" size="sm" class="-mx-2.5 h-8" @click="toggle">
    {{ title }}
    <ArrowUp v-if="column.getIsSorted() === 'asc'" class="size-3.5" />
    <ArrowDown v-else-if="column.getIsSorted() === 'desc'" class="size-3.5" />
    <ChevronsUpDown v-else class="size-3.5 opacity-40" />
  </Button>
</template>

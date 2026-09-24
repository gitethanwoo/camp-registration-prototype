<script setup lang="ts" generic="TData">
import type { Column, ColumnDef, SortingState } from '@tanstack/vue-table'
import type { HTMLAttributes } from 'vue'
import { FlexRender, getCoreRowModel, getSortedRowModel, useVueTable } from '@tanstack/vue-table'
import { Skeleton } from '@/components/ui/skeleton'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { cn, valueUpdater } from '@/lib/utils'

const props = withDefaults(
  defineProps<{
    columns: ColumnDef<TData, any>[]
    /** `null`/`undefined` renders skeleton rows. */
    data: TData[] | null | undefined
    /** Dims the rows while a refetch is in flight. */
    loading?: boolean
    /** Sorting is done by the server; the table only reports the requested order. */
    manualSorting?: boolean
    getRowId?: (row: TData) => string
    onRowClick?: (row: TData) => void
    emptyText?: string
    skeletonRows?: number
    class?: HTMLAttributes['class']
  }>(),
  { emptyText: 'No results.', skeletonRows: 5 },
)

const sorting = defineModel<SortingState>('sorting', { default: () => [] })

const table = useVueTable({
  get data() {
    return props.data ?? []
  },
  get columns() {
    return props.columns
  },
  getCoreRowModel: getCoreRowModel(),
  getSortedRowModel: getSortedRowModel(),
  manualSorting: props.manualSorting,
  enableSortingRemoval: false,
  defaultColumn: { enableSorting: false },
  ...(props.getRowId ? { getRowId: props.getRowId } : {}),
  state: {
    get sorting() {
      return sorting.value
    },
  },
  onSortingChange: (u) => valueUpdater(u, sorting),
})

function ariaSort(column: Column<TData, unknown>) {
  if (!column.getCanSort()) return undefined
  const s = column.getIsSorted()
  return s === 'asc' ? 'ascending' : s === 'desc' ? 'descending' : 'none'
}
function cellClass(column: Column<TData, unknown>, row: TData) {
  const meta = column.columnDef.meta
  return cn(meta?.class, typeof meta?.cellClass === 'function' ? meta.cellClass(row) : meta?.cellClass)
}
function rowAttrs(row: TData) {
  const click = props.onRowClick
  if (!click) return {}
  return {
    class: 'cursor-pointer',
    tabindex: 0,
    onClick: () => click(row),
    onKeydown: (e: KeyboardEvent) => {
      if (e.key === 'Enter') click(row)
    },
  }
}
</script>

<template>
  <div :class="cn('rounded-lg border', props.class)">
    <Table>
      <TableHeader>
        <TableRow v-for="group in table.getHeaderGroups()" :key="group.id">
          <TableHead
            v-for="header in group.headers"
            :key="header.id"
            :class="header.column.columnDef.meta?.class"
            :aria-sort="ariaSort(header.column)"
          >
            <FlexRender
              v-if="!header.isPlaceholder"
              :render="header.column.columnDef.header"
              :props="header.getContext()"
            />
          </TableHead>
        </TableRow>
      </TableHeader>
      <TableBody :class="cn(loading && data && 'opacity-60')">
        <template v-if="!data">
          <TableRow v-for="i in skeletonRows" :key="i">
            <TableCell :colspan="columns.length"><Skeleton class="h-5" /></TableCell>
          </TableRow>
        </template>
        <template v-else-if="table.getRowModel().rows.length">
          <TableRow v-for="row in table.getRowModel().rows" :key="row.id" v-bind="rowAttrs(row.original)">
            <TableCell
              v-for="cell in row.getVisibleCells()"
              :key="cell.id"
              :class="cellClass(cell.column, row.original)"
            >
              <!-- Per-column slot (`#cell-<id>`) for cells that need components or events. -->
              <slot :name="`cell-${cell.column.id}`" :row="row.original" :value="cell.getValue()">
                <FlexRender :render="cell.column.columnDef.cell" :props="cell.getContext()" />
              </slot>
            </TableCell>
          </TableRow>
        </template>
        <TableRow v-else>
          <TableCell :colspan="columns.length" class="h-24 text-center text-muted-foreground">
            <slot name="empty">{{ emptyText }}</slot>
          </TableCell>
        </TableRow>
      </TableBody>
    </Table>
  </div>
</template>

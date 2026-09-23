import type { RowData } from '@tanstack/vue-table'
import type { HTMLAttributes } from 'vue'

export { default as DataTable } from './DataTable.vue'
export { default as DataTableColumnHeader } from './DataTableColumnHeader.vue'

declare module '@tanstack/vue-table' {
  interface ColumnMeta<TData extends RowData, TValue> {
    /** Applied to both the header and body cells (alignment, responsive hiding). */
    class?: HTMLAttributes['class']
    /** Applied to body cells only; may depend on the row. */
    cellClass?: HTMLAttributes['class'] | ((row: TData) => HTMLAttributes['class'])
  }
}

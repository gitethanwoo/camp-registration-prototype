<script setup lang="ts">
import { Badge } from '@/components/ui/badge'
import type { InvoiceStatus } from './types'

defineProps<{ status: InvoiceStatus }>()
</script>

<template>
  <Badge
    variant="outline"
    :class="[
      'font-medium',
      status === 'Paid'
        ? 'bg-emerald-50 text-emerald-800 border-emerald-200'
        : 'bg-amber-50 text-amber-800 border-amber-200',
    ]"
    >{{ status }}</Badge
  >
</template>

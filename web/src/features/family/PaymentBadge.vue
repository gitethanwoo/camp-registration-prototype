<script setup lang="ts">
import { Badge } from '@/components/ui/badge'
import { cn } from '@/lib/utils'
import type { PaymentStatus } from './types'

// Payment vocabulary from the global contract: Paid · Deposit paid · Balance due · Plan active ·
// Installment failed · Authorized (not charged) · Refunded · Voided.
const tone: Record<string, string> = {
  Paid: 'bg-emerald-50 text-emerald-800 border-emerald-200',
  'Deposit paid': 'bg-sky-50 text-sky-800 border-sky-200',
  'Plan active': 'bg-sky-50 text-sky-800 border-sky-200',
  'Balance due': 'bg-amber-50 text-amber-800 border-amber-200',
  'Installment failed': 'bg-red-50 text-red-800 border-red-200',
  Refunded: 'bg-muted text-muted-foreground',
}
defineProps<{ status: PaymentStatus }>()
</script>

<template>
  <Badge v-if="status" variant="outline" :class="cn('font-medium', tone[status])">{{ status }}</Badge>
</template>

<script setup lang="ts">
import { Badge } from '@/components/ui/badge'
import { cn } from '@/lib/utils'

// Payment status in the global vocabulary: Paid, Deposit paid, Balance due, Plan active, Installment failed…
const tone: Record<string, string> = {
  Paid: 'bg-emerald-50 text-emerald-800 border-emerald-200',
  'Plan active': 'bg-sky-50 text-sky-800 border-sky-200',
  'Balance due': 'bg-amber-50 text-amber-800 border-amber-200',
  'Deposit paid': 'bg-amber-50 text-amber-800 border-amber-200',
  'Installment failed': 'bg-red-50 text-red-800 border-red-200',
  Refunded: 'bg-muted text-muted-foreground',
  Voided: 'bg-muted text-muted-foreground',
}
defineProps<{ status: string }>()
</script>

<template>
  <Badge variant="outline" :class="cn('font-medium', tone[status])">{{ status }}</Badge>
</template>

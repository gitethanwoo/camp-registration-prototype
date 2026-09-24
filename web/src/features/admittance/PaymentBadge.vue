<script setup lang="ts">
import { computed } from 'vue'
import { Badge } from '@/components/ui/badge'
import { cn } from '@/lib/utils'
import type { PaymentState } from './types'

const props = defineProps<{ state: PaymentState }>()

// "Authorized" always says "not charged": families and staff both read it as money taken otherwise.
const look: Record<PaymentState, { label: string; class: string }> = {
  Authorized: { label: 'Authorized (not charged)', class: 'bg-sky-50 text-sky-800 border-sky-200' },
  Expiring: { label: 'Authorization expiring', class: 'bg-amber-50 text-amber-800 border-amber-200' },
  Expired: { label: 'Authorization expired', class: 'bg-red-50 text-red-800 border-red-200' },
  CardNeeded: { label: 'New card needed', class: 'bg-amber-50 text-amber-800 border-amber-200' },
  Paid: { label: 'Paid', class: 'bg-emerald-50 text-emerald-800 border-emerald-200' },
  Voided: { label: 'Voided', class: 'bg-muted text-muted-foreground' },
  None: { label: 'No card yet', class: 'bg-muted text-muted-foreground' },
}
const v = computed(() => look[props.state])
</script>

<template>
  <Badge variant="outline" :class="cn('font-medium', v.class)">{{ v.label }}</Badge>
</template>

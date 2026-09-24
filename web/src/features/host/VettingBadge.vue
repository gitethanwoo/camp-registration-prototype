<script setup lang="ts">
import { CircleCheck, CircleDashed, Clock } from '@lucide/vue'
import { Badge } from '@/components/ui/badge'
import type { VettingStatus } from './types'

defineProps<{ status: VettingStatus }>()

// Vetting vocabulary from the H2 card: Approved, In progress, Not started.
const look = {
  Approved: {
    label: 'Approved',
    icon: CircleCheck,
    class: 'bg-emerald-50 text-emerald-800 border-emerald-200',
  },
  InProgress: {
    label: 'In progress',
    icon: Clock,
    class: 'bg-amber-50 text-amber-800 border-amber-200',
  },
  NotStarted: {
    label: 'Not started',
    icon: CircleDashed,
    class: 'bg-muted text-muted-foreground',
  },
} as const
</script>

<template>
  <Badge variant="outline" :class="['gap-1 font-medium', look[status].class]"
    ><component :is="look[status].icon" class="size-3" />{{ look[status].label }}</Badge
  >
</template>

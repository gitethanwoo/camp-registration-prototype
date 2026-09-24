<script setup lang="ts">
import { CircleCheck, CircleX, LogIn, LogOut } from '@lucide/vue'
import { Badge } from '@/components/ui/badge'
import type { CheckInStatus } from './types'

// O5 status words: Ready, Blocked, Checked in, Checked out.
defineProps<{ status: CheckInStatus }>()
const tone: Record<CheckInStatus, string> = {
  Ready: 'border-emerald-200 bg-emerald-50 text-emerald-800',
  Blocked: 'border-red-200 bg-red-50 text-red-800',
  'Checked in': 'border-sky-200 bg-sky-50 text-sky-800',
  'Checked out': 'bg-muted text-muted-foreground',
}
const icon = { Ready: CircleCheck, Blocked: CircleX, 'Checked in': LogIn, 'Checked out': LogOut }
</script>

<template>
  <Badge variant="outline" :class="tone[status]">
    <component :is="icon[status]" class="size-3" aria-hidden="true" />{{ status }}
  </Badge>
</template>

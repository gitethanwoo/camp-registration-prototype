<script setup lang="ts">
import type { Component } from 'vue'
import { Card, CardContent } from '@/components/ui/card'
import { Progress } from '@/components/ui/progress'
import { cn } from '@/lib/utils'

// A labelled number with an optional progress bar, used by every operations page.
const props = defineProps<{
  label: string
  value: string | number
  of?: number
  hint?: string
  icon: Component
  tone?: 'good' | 'warn' | 'neutral'
  percent?: number
}>()
const iconTone = {
  good: 'text-emerald-600',
  warn: 'text-amber-600',
  neutral: 'text-muted-foreground',
}
</script>

<template>
  <Card class="gap-0 py-4">
    <CardContent class="flex items-start gap-3 px-4">
      <component
        :is="props.icon"
        :class="cn('mt-0.5 size-6 shrink-0', iconTone[props.tone ?? 'neutral'])"
        aria-hidden="true"
      />
      <div class="min-w-0 flex-1">
        <p class="text-sm text-muted-foreground">{{ props.label }}</p>
        <p class="text-2xl font-semibold tabular-nums">
          {{ props.value
          }}<span v-if="props.of != null" class="text-base font-normal text-muted-foreground"> / {{ props.of }}</span>
        </p>
        <Progress
          v-if="props.percent != null"
          :model-value="props.percent"
          :aria-label="`${props.label}: ${props.percent}%`"
          class="mt-2 h-1.5 [&>*]:bg-emerald-600"
        />
        <p v-if="props.hint" class="mt-1 text-xs text-muted-foreground">{{ props.hint }}</p>
      </div>
    </CardContent>
  </Card>
</template>

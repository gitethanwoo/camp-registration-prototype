<script setup lang="ts">
import { Card, CardContent } from '@/components/ui/card'

// A headline number on a finance screen: label, value, and a line of context.
defineProps<{ label: string; value: string | number; sub?: string }>()
</script>

<template>
  <Card class="gap-0 py-4">
    <CardContent class="flex items-start gap-3 px-4">
      <div v-if="$slots.icon" class="rounded-full bg-muted p-2 text-muted-foreground [&_svg]:size-5">
        <slot name="icon" />
      </div>
      <div class="min-w-0">
        <p class="text-sm text-muted-foreground">{{ label }}</p>
        <p class="text-2xl font-semibold tabular-nums">{{ value }}</p>
        <p v-if="sub" class="text-xs text-muted-foreground">{{ sub }}</p>
        <slot />
      </div>
    </CardContent>
  </Card>
</template>

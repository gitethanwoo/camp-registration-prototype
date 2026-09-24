<script setup lang="ts">
import { Button } from '@/components/ui/button'
import { date, dateTime } from '@/lib/format'
import PublishBadge from '@/features/setup/PublishBadge.vue'
import type { FormVersionView } from './types'

// K6 · every version of a program's form, newest first. Retired versions stay readable so staff can
// see what a family was asked.
defineProps<{ versions: FormVersionView[]; viewedId: number | null }>()
const emit = defineEmits<{ view: [id: number] }>()
</script>

<template>
  <ol class="divide-y">
    <li v-for="v in versions" :key="v.id" class="grid gap-3 py-4 first:pt-0 sm:grid-cols-[1fr_auto] sm:items-start">
      <div class="min-w-0 space-y-1 text-sm">
        <p class="flex flex-wrap items-center gap-2">
          <span class="font-medium">Version {{ v.version }}</span>
          <PublishBadge :state="v.status" :label="v.statusLabel" />
          <span class="text-muted-foreground">{{ v.questions.length }} questions</span>
        </p>
        <p v-if="v.changeNote">{{ v.changeNote }}</p>
        <p class="text-muted-foreground">
          Written by {{ v.createdBy }}, {{ dateTime(v.createdAt) }}
          <template v-if="v.approvedBy">· approved by {{ v.approvedBy }}, {{ dateTime(v.approvedAt!) }}</template>
          <template v-if="v.retiredAt">· replaced {{ date(v.retiredAt) }}</template>
        </p>
        <p v-if="v.status === 'Published' || v.status === 'Retired'" class="text-muted-foreground">
          {{ v.registrations }} {{ v.registrations === 1 ? 'camper' : 'campers' }} answered this version
        </p>
      </div>
      <Button
        size="sm"
        :variant="v.id === viewedId ? 'secondary' : 'outline'"
        :aria-current="v.id === viewedId || undefined"
        :aria-label="`View version ${v.version}`"
        @click="emit('view', v.id)"
        >{{ v.id === viewedId ? 'Viewing' : 'View' }}</Button
      >
    </li>
  </ol>
</template>

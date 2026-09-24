<script setup lang="ts">
import { Check } from '@lucide/vue'
import { ref, watch } from 'vue'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Sheet, SheetContent, SheetDescription, SheetFooter, SheetHeader, SheetTitle } from '@/components/ui/sheet'
import { Skeleton } from '@/components/ui/skeleton'
import { api, ApiError } from '@/lib/api'
import ActivityDetailBody from './ActivityDetailBody.vue'
import type { ActivityDetail } from './types'

// P3 as a sheet from R4 and the family activities page, with "Select for {name}" for one period.
const props = defineProps<{
  activityId: number | null
  sessionId: number
  camperName: string
  block: string | null
  period: number | null
  selected: boolean
  canSelect: boolean
}>()
const emit = defineEmits<{ close: []; select: [] }>()
const detail = ref<ActivityDetail | null>(null)
const error = ref<string | null>(null)

watch(
  () => props.activityId,
  async (id) => {
    detail.value = null
    error.value = null
    if (!id) return
    try {
      detail.value = await api.get<ActivityDetail>(`/activities/${id}?sessionId=${props.sessionId}`)
    } catch (e) {
      error.value =
        e instanceof ApiError && e.status === 404
          ? 'This activity is no longer offered.'
          : "Details didn't load. Try again."
    }
  },
  { immediate: true },
)
</script>

<template>
  <Sheet :open="activityId !== null" @update:open="(v) => !v && emit('close')">
    <SheetContent class="w-full overflow-y-auto sm:max-w-lg">
      <SheetHeader>
        <SheetTitle class="text-xl">{{ detail?.name ?? 'Activity' }}</SheetTitle>
        <SheetDescription v-if="period">Period {{ period }} for {{ camperName }}</SheetDescription>
      </SheetHeader>
      <div class="px-4">
        <Alert v-if="error" variant="destructive"
          ><AlertDescription>{{ error }}</AlertDescription></Alert
        >
        <ActivityDetailBody v-else-if="detail" :activity="detail" :block="block" />
        <Skeleton v-else class="h-80 rounded-xl" />
      </div>
      <SheetFooter v-if="period && detail" class="flex-row flex-wrap gap-2">
        <Button v-if="selected" variant="outline" @click="emit('close')"><Check />Selected for {{ camperName }}</Button>
        <Button v-else :disabled="!canSelect" @click="emit('select')">Select for {{ camperName }}</Button>
        <p v-if="!selected && !canSelect" class="w-full text-sm text-muted-foreground">
          Full in Period {{ period }}, or you've already ranked three. Remove one to add this.
        </p>
      </SheetFooter>
    </SheetContent>
  </Sheet>
</template>

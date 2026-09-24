<script setup lang="ts">
import { ArrowLeft } from '@lucide/vue'
import { onMounted, ref } from 'vue'
import { RouterLink } from 'vue-router'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/skeleton'
import { api, ApiError } from '@/lib/api'
import ActivityDetailBody from './ActivityDetailBody.vue'
import type { ActivityDetail } from './types'

// P3 · Activity detail (FR-24): public, phone first.
const props = defineProps<{ id: number; sessionId: number | null }>()
const detail = ref<ActivityDetail | null>(null)
const error = ref<string | null>(null)

onMounted(async () => {
  try {
    detail.value = await api.get<ActivityDetail>(
      `/activities/${props.id}${props.sessionId ? `?sessionId=${props.sessionId}` : ''}`,
    )
  } catch (e) {
    error.value =
      e instanceof ApiError && e.status === 404
        ? 'This activity is no longer offered.'
        : "This activity didn't load. Refresh to try again."
  }
})
</script>

<template>
  <div class="mx-auto max-w-3xl px-4 py-6 md:py-10">
    <Button variant="ghost" size="sm" class="-ml-2.5 mb-4 text-muted-foreground" as-child>
      <RouterLink to="/programs/overnight-camp"><ArrowLeft />Overnight Camp</RouterLink>
    </Button>
    <Alert v-if="error" variant="destructive">
      <AlertTitle>Activity not available</AlertTitle>
      <AlertDescription>{{ error }}</AlertDescription>
    </Alert>
    <template v-else-if="detail">
      <h1 class="mb-4 text-2xl font-semibold tracking-tight md:text-3xl">{{ detail.name }}</h1>
      <ActivityDetailBody :activity="detail" />
    </template>
    <Skeleton v-else class="h-96 rounded-xl" />
  </div>
</template>

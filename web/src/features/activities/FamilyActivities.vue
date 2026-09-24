<script setup lang="ts">
import { ArrowLeft, TriangleAlert } from '@lucide/vue'
import { computed, onMounted, reactive, ref } from 'vue'
import { RouterLink, useRouter } from 'vue-router'
import { toast } from 'vue-sonner'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/skeleton'
import { api, ApiError } from '@/lib/api'
import { dateRange } from '@/lib/format'
import ActivityDetailSheet from './ActivityDetailSheet.vue'
import PeriodPicker from './PeriodPicker.vue'
import { isFull, periodProblem, toggleRank, useInstead } from './ranking'
import type { FamilyActivities } from './types'

// F1 "Choose activities": R4 for one registered camper, after registration.
const props = defineProps<{ registrationId: number }>()
const router = useRouter()
const data = ref<FamilyActivities | null>(null)
const error = ref<string | null>(null)
const ranked = reactive<Record<number, number[]>>({})
const attempted = ref(false)
const saving = ref(false)
const message = ref<string | null>(null)
const MAX = 3

async function load(keepChoices = false) {
  try {
    data.value = await api.get<FamilyActivities>(`/family/activities/${props.registrationId}`)
    if (!keepChoices) for (const c of data.value.choices) ranked[c.period] = [...c.ranked]
  } catch (e) {
    error.value =
      e instanceof ApiError && e.status === 404
        ? "We couldn't find that registration in your family, or its session has no activity schedule."
        : "Activities didn't load. Refresh to try again."
  }
}
onMounted(() => load())

const problems = computed(() =>
  (data.value?.periods ?? [])
    .map((p) => periodProblem(data.value?.firstName ?? '', p, ranked[p.period] ?? []))
    .filter((x): x is string => !!x),
)
const placed = computed(() => (data.value?.placed ?? []).filter((p) => p.name).length)

async function save() {
  attempted.value = true
  message.value = null
  if (problems.value.length) return
  saving.value = true
  try {
    const choices = Object.entries(ranked)
      .filter(([, ids]) => ids.length)
      .map(([period, ids]) => ({ period: Number(period), ranked: ids }))
    await api.put(`/family/activities/${props.registrationId}`, { choices })
    toast.success(`${data.value?.firstName}'s activities are saved.`)
    router.push('/family')
  } catch (e) {
    if (e instanceof ApiError && e.status === 409) {
      message.value = e.message
      await load(true)
    } else message.value = e instanceof ApiError ? e.message : "That didn't save. Check your connection and try again."
  } finally {
    saving.value = false
  }
}

const sheet = ref<{ activityId: number; period: number } | null>(null)
const sheetOption = computed(() =>
  data.value?.periods
    .find((p) => p.period === sheet.value?.period)
    ?.options.find((o) => o.activityId === sheet.value?.activityId),
)
</script>

<template>
  <div class="mx-auto max-w-6xl px-4 py-6 md:py-10">
    <Button variant="ghost" size="sm" class="-ml-2.5 mb-4 text-muted-foreground" as-child>
      <RouterLink to="/family"><ArrowLeft />Family home</RouterLink>
    </Button>
    <Alert v-if="error" variant="destructive" class="max-w-lg">
      <TriangleAlert class="size-4" />
      <AlertTitle>Can't choose activities</AlertTitle>
      <AlertDescription>{{ error }}</AlertDescription>
    </Alert>
    <div v-else-if="!data" class="grid gap-4">
      <Skeleton v-for="i in 3" :key="i" class="h-72 rounded-xl" />
    </div>
    <template v-else>
      <p class="text-sm font-medium tracking-wide text-muted-foreground uppercase">{{ data.session }}</p>
      <h1 class="text-2xl font-semibold tracking-tight md:text-3xl">Choose activities for {{ data.firstName }}</h1>
      <p class="mt-1 text-muted-foreground">
        {{ dateRange(data.startDate, data.endDate) }} · {{ data.gradeLabel }} · {{ data.block }}.
        <template v-if="placed">Placed in {{ placed }} of 3 periods. Saving replaces those places.</template>
        <template v-else>Rank up to three per period; we place {{ data.firstName }} in the first with room.</template>
      </p>

      <Alert v-if="message" variant="destructive" class="mt-6" aria-live="assertive">
        <TriangleAlert class="size-4" />
        <AlertTitle>Not saved</AlertTitle>
        <AlertDescription>{{ message }}</AlertDescription>
      </Alert>

      <div class="mt-6 grid gap-4">
        <PeriodPicker
          v-for="p in data.periods"
          :key="p.period"
          :period="p"
          :ranked="ranked[p.period] ?? []"
          :max-ranks="MAX"
          :camper-name="data.firstName"
          :invalid="attempted && !!periodProblem('', p, ranked[p.period] ?? [])"
          @toggle="(id) => (ranked[p.period] = toggleRank(ranked[p.period] ?? [], id, MAX))"
          @use-instead="(id) => (ranked[p.period] = useInstead(p, ranked[p.period] ?? [], id))"
          @details="(id) => (sheet = { activityId: id, period: p.period })"
        />
      </div>

      <Alert v-if="attempted && problems.length" variant="destructive" class="mt-6">
        <TriangleAlert class="size-4" />
        <AlertTitle>Before you save</AlertTitle>
        <AlertDescription
          ><ul class="list-disc pl-4">
            <li v-for="p in problems" :key="p">{{ p }}</li>
          </ul></AlertDescription
        >
      </Alert>
      <div class="mt-6 flex items-center justify-end gap-3">
        <Button variant="ghost" as-child><RouterLink to="/family">Cancel</RouterLink></Button>
        <Button size="lg" :disabled="saving" @click="save">{{ saving ? 'Saving…' : 'Save activities' }}</Button>
      </div>

      <ActivityDetailSheet
        :activity-id="sheet?.activityId ?? null"
        :session-id="data.sessionId"
        :camper-name="data.firstName"
        :block="data.block"
        :period="sheet?.period ?? null"
        :selected="!!sheet && (ranked[sheet.period] ?? []).includes(sheet.activityId)"
        :can-select="!!sheetOption && !isFull(sheetOption) && (ranked[sheet?.period ?? 0] ?? []).length < MAX"
        @close="sheet = null"
        @select="
          () => {
            if (sheet) ranked[sheet.period] = toggleRank(ranked[sheet.period] ?? [], sheet.activityId, MAX)
            sheet = null
          }
        "
      />
    </template>
  </div>
</template>

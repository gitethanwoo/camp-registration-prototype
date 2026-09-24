<script setup lang="ts">
import { AlertTriangle, BarChart3, CalendarClock, FileText, TriangleAlert, Users } from '@lucide/vue'
import { computed, onMounted, ref } from 'vue'
import { RouterLink } from 'vue-router'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Progress } from '@/components/ui/progress'
import { Skeleton } from '@/components/ui/skeleton'
import { api, ApiError } from '@/lib/api'
import { date, dateRange, money } from '@/lib/format'
import type { Overview } from './types'

// H1 · Host home: this year's registrations against capacity and last year, volunteers, the
// invoice balance, and what's due next (FR-87).
const data = ref<Overview | null>(null)
const error = ref<string | null>(null)

onMounted(async () => {
  try {
    data.value = await api.get<Overview>('/host/overview')
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : "Couldn't load your overview."
  }
})

const ev = computed(() => data.value?.event ?? null)
const programShort = computed(() => ev.value?.program.split(' · ')[0] ?? 'Host')
const fullPct = computed(() =>
  ev.value && ev.value.capacity > 0 ? Math.round((ev.value.registrations / ev.value.capacity) * 100) : 0,
)
const change = computed(() => (ev.value ? ev.value.registrations - ev.value.lastYear : 0))
// Chart scale: the larger of capacity and last year, so both bars fit.
const scaleMax = computed(() =>
  Math.max(ev.value?.capacity ?? 0, ev.value?.lastYear ?? 0, ev.value?.registrations ?? 0, 1),
)
const bars = computed(() =>
  ev.value
    ? [
        {
          label: 'This year',
          value: ev.value.registrations,
          class: 'bg-primary',
        },
        {
          label: 'Last year',
          value: ev.value.lastYear,
          class: 'bg-muted-foreground/40',
        },
      ]
    : [],
)
const deadlineIcon = {
  upload: TriangleAlert,
  vetting: CalendarClock,
  invoice: FileText,
} as const
</script>

<template>
  <div class="mx-auto max-w-6xl space-y-6">
    <div>
      <h1 class="text-2xl font-semibold tracking-tight md:text-3xl">{{ programShort }} host overview</h1>
      <p v-if="data" class="mt-1 text-muted-foreground">
        {{ data.organization }} · {{ data.city
        }}<template v-if="ev"> · {{ dateRange(ev.startDate, ev.endDate) }}</template>
      </p>
      <Skeleton v-else-if="!error" class="mt-2 h-5 w-72" />
    </div>

    <Alert v-if="error" variant="destructive">
      <AlertTriangle />
      <AlertDescription>{{ error }}</AlertDescription>
    </Alert>

    <template v-if="data">
      <div class="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <Card>
          <CardContent class="flex gap-4">
            <Users class="size-6 shrink-0 text-muted-foreground" />
            <div class="min-w-0 flex-1 space-y-2">
              <p class="text-sm text-muted-foreground">Registrations</p>
              <p v-if="ev" class="text-2xl font-semibold tabular-nums" data-testid="registrations">
                {{ ev.registrations }} / {{ ev.capacity }}
              </p>
              <p v-else class="text-sm">No event assigned yet.</p>
              <div v-if="ev" class="flex items-center gap-2">
                <Progress :model-value="Math.min(fullPct, 100)" class="h-2" :aria-label="`${fullPct}% full`" />
                <span class="shrink-0 text-xs text-muted-foreground">{{ fullPct }}% full</span>
              </div>
            </div>
          </CardContent>
        </Card>
        <Card v-if="ev">
          <CardContent class="flex gap-4">
            <BarChart3 class="size-6 shrink-0 text-muted-foreground" />
            <div class="space-y-1">
              <p class="text-sm text-muted-foreground">Last year</p>
              <p class="text-2xl font-semibold tabular-nums">
                {{ ev.lastYear }}
              </p>
              <p class="text-sm text-muted-foreground">
                registrations<template v-if="change !== 0">
                  · {{ Math.abs(change) }} {{ change > 0 ? 'more' : 'fewer' }} this year</template
                >
              </p>
            </div>
          </CardContent>
        </Card>
        <Card>
          <CardContent class="flex gap-4">
            <Users class="size-6 shrink-0 text-muted-foreground" />
            <div class="min-w-0 flex-1 space-y-1">
              <div class="flex items-start justify-between gap-2">
                <p class="text-sm text-muted-foreground">Volunteers</p>
                <TriangleAlert
                  v-if="data.upload.needsDecision > 0"
                  class="size-5 text-amber-600"
                  aria-label="Upload rows need fixes"
                />
              </div>
              <p class="text-2xl font-semibold tabular-nums">{{ data.volunteers.total }} on file</p>
              <p class="text-sm text-muted-foreground">
                {{ data.volunteers.approved }} approved
                <template v-if="data.upload.needsDecision > 0">
                  ·
                  <span class="text-amber-700"
                    >{{ data.upload.needsDecision }} upload
                    {{ data.upload.needsDecision === 1 ? 'row needs' : 'rows need' }}
                    fixes</span
                  ></template
                >
              </p>
            </div>
          </CardContent>
        </Card>
        <Card>
          <CardContent class="flex gap-4">
            <FileText class="size-6 shrink-0 text-muted-foreground" />
            <div class="min-w-0 flex-1 space-y-1">
              <div class="flex items-start justify-between gap-2">
                <p class="text-sm text-muted-foreground">Invoice balance</p>
                <TriangleAlert v-if="data.invoices.balanceCents > 0" class="size-5 text-amber-600" aria-hidden="true" />
              </div>
              <p class="text-2xl font-semibold tabular-nums" data-testid="invoice-balance">
                {{ data.invoices.balanceCents > 0 ? `${money(data.invoices.balanceCents)} due` : 'Paid up' }}
              </p>
              <p class="text-sm text-muted-foreground">
                <template v-if="data.invoices.nextDue">Due {{ date(data.invoices.nextDue.dueDate) }}</template>
                <template v-else>No open invoices</template>
              </p>
            </div>
          </CardContent>
        </Card>
      </div>

      <div class="grid gap-4 lg:grid-cols-2">
        <Card v-if="ev">
          <CardHeader>
            <CardTitle>Registrations</CardTitle>
            <CardDescription>This year vs. last year · {{ ev.session }}</CardDescription>
          </CardHeader>
          <CardContent>
            <figure
              class="flex h-56 items-end justify-around gap-6 border-b px-4"
              :aria-label="`Registrations: ${ev.registrations} this year, ${ev.lastYear} last year`"
            >
              <div v-for="b in bars" :key="b.label" class="flex h-full w-24 flex-col items-center justify-end gap-1">
                <span class="text-sm font-semibold tabular-nums">{{ b.value }}</span>
                <div :class="['w-full rounded-t-md', b.class]" :style="{ height: `${(b.value / scaleMax) * 85}%` }" />
              </div>
            </figure>
            <div class="flex justify-around gap-6 px-4 pt-2 text-sm text-muted-foreground">
              <span v-for="b in bars" :key="b.label" class="w-24 text-center">{{ b.label }}</span>
            </div>
            <p class="mt-3 text-xs text-muted-foreground">
              Capacity {{ ev.capacity }}. Counts registrations holding a spot or on the way to one; waitlisted and
              cancelled are left out.
            </p>
          </CardContent>
        </Card>

        <Card :class="ev ? '' : 'lg:col-span-2'">
          <CardHeader>
            <CardTitle>Upcoming deadlines</CardTitle>
          </CardHeader>
          <CardContent>
            <p v-if="!data.deadlines.length" class="text-sm text-muted-foreground">
              Nothing due. You're all caught up.
            </p>
            <ul class="divide-y" data-testid="deadlines">
              <li v-for="d in data.deadlines" :key="d.title" class="flex items-start gap-3 py-4 first:pt-0 last:pb-0">
                <component
                  :is="deadlineIcon[d.kind]"
                  :class="['size-5 shrink-0', d.kind === 'vetting' ? 'text-muted-foreground' : 'text-amber-600']"
                  aria-hidden="true"
                />
                <div class="flex min-w-0 flex-1 flex-col gap-3 sm:flex-row sm:items-start">
                  <div class="min-w-0 flex-1">
                    <p class="font-medium">{{ d.title }}</p>
                    <p class="text-sm text-muted-foreground">{{ d.detail }}</p>
                  </div>
                  <Button variant="outline" size="sm" as-child class="self-start">
                    <RouterLink :to="d.link">{{ d.linkLabel }}</RouterLink>
                  </Button>
                </div>
              </li>
            </ul>
          </CardContent>
        </Card>
      </div>
    </template>

    <div v-else-if="!error" class="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
      <Skeleton v-for="i in 4" :key="i" class="h-28 rounded-xl" />
    </div>
  </div>
</template>

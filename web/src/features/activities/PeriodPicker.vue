<script setup lang="ts">
import { TriangleAlert } from '@lucide/vue'
import { computed } from 'vue'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { cn } from '@/lib/utils'
import { filledChoices, isFull, slotsLeft, suggestion } from './ranking'
import type { ActivityOption, PeriodOptions } from './types'

// One period of R4: tap to rank up to `maxRanks` activities. A ranked choice that fills shows
// "Just filled" with an amber notice and the suggested next choice.
const props = defineProps<{
  period: PeriodOptions
  ranked: number[]
  maxRanks: number
  camperName: string
  invalid?: boolean
}>()
const emit = defineEmits<{ toggle: [id: number]; useInstead: [id: number]; details: [id: number] }>()

const rank = (o: ActivityOption) => props.ranked.indexOf(o.activityId) + 1
const filled = computed(() => filledChoices(props.period, props.ranked))
const suggest = computed(() => (filled.value.length ? suggestion(props.period, props.ranked) : null))
// The notice sits right under the first choice that filled.
const noticeAfter = computed(() => filled.value[0]?.activityId)
const suggestIsRanked = computed(() => !!suggest.value && props.ranked.includes(suggest.value.activityId))
const canAdd = (o: ActivityOption) => rank(o) > 0 || (!isFull(o) && props.ranked.length < props.maxRanks)
</script>

<template>
  <Card :class="cn('gap-4', invalid && 'border-destructive')" :data-testid="`period-${period.period}`">
    <CardHeader>
      <CardTitle>Period {{ period.period }}</CardTitle>
      <CardDescription>{{ period.time }} · rank up to {{ maxRanks }}</CardDescription>
    </CardHeader>
    <CardContent class="grid gap-2 sm:grid-cols-2">
      <template v-for="o in period.options" :key="o.activityId">
        <div
          :class="
            cn(
              'flex items-center gap-1 rounded-lg border pr-1',
              rank(o) > 0 && !isFull(o) && 'border-primary bg-primary/5',
              isFull(o) && 'bg-muted/60',
            )
          "
        >
          <Button
            variant="ghost"
            class="h-auto min-w-0 flex-1 justify-start gap-3 px-3 py-2.5 text-left font-normal whitespace-normal hover:bg-transparent"
            :aria-pressed="rank(o) > 0"
            :aria-label="`${o.name}, Period ${period.period}${rank(o) ? `, choice ${rank(o)}` : ''}: ${slotsLeft(o)}`"
            :disabled="!canAdd(o)"
            @click="emit('toggle', o.activityId)"
          >
            <span
              :class="
                cn(
                  'flex size-7 shrink-0 items-center justify-center rounded-full border text-xs font-medium',
                  rank(o) > 0 && !isFull(o) && 'border-primary bg-primary text-primary-foreground',
                )
              "
              >{{ rank(o) || '' }}</span
            >
            <span class="min-w-0">
              <span :class="cn('flex flex-wrap items-center gap-x-2 font-medium', isFull(o) && 'text-muted-foreground')"
                >{{ o.name }}
                <Badge
                  v-if="isFull(o) && rank(o)"
                  variant="secondary"
                  class="border-amber-200 bg-amber-50 font-normal text-amber-800"
                  >Just filled</Badge
                >
                <Badge v-else-if="isFull(o)" variant="outline" class="font-normal text-muted-foreground">Full</Badge>
              </span>
              <span
                :class="
                  cn(
                    'block text-xs',
                    o.remaining > 0 && o.remaining <= 5 ? 'font-medium text-amber-700' : 'text-muted-foreground',
                  )
                "
                >{{ isFull(o) && rank(o) ? 'No longer available' : slotsLeft(o) }}</span
              >
            </span>
          </Button>
          <Button
            variant="link"
            size="sm"
            class="shrink-0 px-2"
            :aria-label="`${o.name} details`"
            @click="emit('details', o.activityId)"
            >Details</Button
          >
        </div>
        <div
          v-if="o.activityId === noticeAfter"
          role="status"
          class="flex gap-2 rounded-lg border border-amber-200 sm:col-span-2 bg-amber-50 p-3 text-sm text-amber-900"
        >
          <TriangleAlert class="mt-0.5 size-4 shrink-0 text-amber-600" />
          <div class="min-w-0 space-y-1">
            <p class="font-medium">
              {{ filled.length === 1 ? `${filled[0]?.name} just filled up` : 'Some of your choices just filled up' }}
            </p>
            <p v-if="suggest && suggestIsRanked">
              We'll place {{ camperName }} in {{ suggest.name }}, your next choice ({{ slotsLeft(suggest) }}).
            </p>
            <p v-else-if="suggest">We suggest {{ suggest.name }} ({{ slotsLeft(suggest) }}).</p>
            <p v-else>Every activity in this period is full. Call the camp office to be placed.</p>
            <Button
              v-if="suggest"
              size="sm"
              variant="outline"
              class="mt-1 border-amber-300 bg-white"
              @click="emit('useInstead', suggest.activityId)"
              >Use {{ suggest.name }}</Button
            >
          </div>
        </div>
      </template>
    </CardContent>
  </Card>
</template>

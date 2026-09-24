<script setup lang="ts">
import { Backpack, GraduationCap, MapPin, UsersRound } from '@lucide/vue'
import { computed } from 'vue'
import { Badge } from '@/components/ui/badge'
import { cn } from '@/lib/utils'
import type { ActivityDetail } from './types'

// P3 body, shared by the public page and the sheet in the registration wizard.
const props = defineProps<{ activity: ActivityDetail; block?: string | null }>()
const schedule = computed(() =>
  props.block ? props.activity.schedule.filter((s) => s.block === props.block) : props.activity.schedule,
)
const left = (n: number) => (n <= 0 ? 'Full' : `${n} ${n === 1 ? 'slot' : 'slots'} left`)
</script>

<template>
  <div class="space-y-5">
    <div class="aspect-[4/3] overflow-hidden rounded-xl bg-muted sm:aspect-[16/9]">
      <img :src="activity.imageUrl" :alt="activity.name" class="size-full object-cover" />
    </div>
    <div class="flex flex-wrap items-center gap-2">
      <Badge variant="secondary">{{ activity.category }}</Badge>
      <Badge variant="outline"><GraduationCap />{{ activity.gradeLabel }}</Badge>
    </div>
    <p class="leading-relaxed">{{ activity.description }}</p>
    <dl class="grid gap-3 text-sm sm:grid-cols-2">
      <div class="flex gap-2">
        <MapPin class="mt-0.5 size-4 shrink-0 text-muted-foreground" />
        <div>
          <dt class="text-muted-foreground">Where</dt>
          <dd>{{ activity.space }}</dd>
        </div>
      </div>
      <div class="flex gap-2">
        <UsersRound class="mt-0.5 size-4 shrink-0 text-muted-foreground" />
        <div>
          <dt class="text-muted-foreground">Staff</dt>
          <dd>
            1 staff member for every {{ activity.staffRatio }} campers<template v-if="activity.instructor"
              >, led by a {{ activity.instructor.toLowerCase() }}</template
            >
          </dd>
        </div>
      </div>
      <div v-if="activity.whatToBring" class="flex gap-2 sm:col-span-2">
        <Backpack class="mt-0.5 size-4 shrink-0 text-muted-foreground" />
        <div>
          <dt class="text-muted-foreground">What to bring</dt>
          <dd>{{ activity.whatToBring }}</dd>
        </div>
      </div>
    </dl>

    <section v-for="s in schedule" :key="s.blockId" class="space-y-2">
      <h3 class="text-sm font-medium">
        {{ s.session }} · {{ s.block }} <span class="font-normal text-muted-foreground">({{ s.grades }})</span>
      </h3>
      <ul class="divide-y rounded-lg border text-sm">
        <li v-for="p in s.periods" :key="p.period" class="flex items-center justify-between gap-3 px-3 py-2">
          <span
            >Period {{ p.period }} <span class="text-muted-foreground">· {{ p.time }}</span></span
          >
          <span
            :class="
              cn(
                'tabular-nums',
                p.remaining <= 0 && 'font-medium text-destructive',
                p.remaining > 0 && p.remaining <= 5 && 'font-medium text-amber-700',
              )
            "
            >{{ left(p.remaining) }}</span
          >
        </li>
      </ul>
    </section>
    <p v-if="!schedule.length" class="text-sm text-muted-foreground">
      Not on the schedule for this session. Choose another activity.
    </p>
  </div>
</template>

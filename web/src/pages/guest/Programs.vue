<script setup lang="ts">
import { Calendar, MapPin, Users } from '@lucide/vue'
import { onMounted, ref } from 'vue'
import { RouterLink } from 'vue-router'
import { Badge } from '@/components/ui/badge'
import { Card, CardContent } from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'
import { api } from '@/lib/api'
import { dateRange, money } from '@/lib/format'
import type { ProgramSummary } from '@/lib/types'

const programs = ref<ProgramSummary[] | null>(null)
const error = ref<string | null>(null)
onMounted(async () => {
  try {
    programs.value = await api.get<ProgramSummary[]>('/programs')
  } catch (e) {
    error.value = (e as Error).message
  }
})

const typeLabels: Record<string, string> = { Admittance: 'Application required', Cohort: 'Group registration' }
const typeLabel = (t: string) => typeLabels[t] ?? null
const audience = (s: ProgramSummary['sessions'][number]) =>
  s.gradeMin >= 99 ? 'Adults' : `Grades ${s.gradeMin}–${s.gradeMax}`
</script>

<template>
  <div class="mx-auto max-w-6xl px-4 py-8 md:py-12">
    <h1 class="text-3xl font-semibold tracking-tight">Programs</h1>
    <p class="mt-2 text-muted-foreground">
      Every WinShape camp, retreat, and cohort in one place, with live availability.
    </p>

    <p v-if="error" class="mt-8 text-destructive">Couldn't load programs: {{ error }}</p>

    <div class="mt-8 grid gap-6 sm:grid-cols-2 lg:grid-cols-3">
      <template v-if="!programs">
        <Skeleton v-for="i in 3" :key="i" class="h-80 rounded-xl" />
      </template>
      <RouterLink
        v-for="p in programs"
        :key="p.slug"
        :to="`/programs/${p.slug}`"
        class="group rounded-xl focus-visible:ring-2 focus-visible:ring-ring focus-visible:outline-none"
      >
        <Card class="h-full gap-0 overflow-hidden py-0 transition-shadow group-hover:shadow-md">
          <div class="aspect-[16/9] bg-muted">
            <img
              v-if="p.imageUrl && !p.imageUrl.includes('retreat') && !p.imageUrl.includes('leaders')"
              :src="p.imageUrl"
              alt=""
              class="size-full object-cover"
            />
            <div v-else class="flex size-full items-center justify-center text-muted-foreground">
              <Users class="size-10" />
            </div>
          </div>
          <CardContent class="flex flex-1 flex-col gap-3 p-5">
            <div class="flex flex-wrap items-center gap-2 text-xs text-muted-foreground">
              <span>{{ p.ministry }}</span>
              <Badge v-if="typeLabel(p.type)" variant="secondary">{{ typeLabel(p.type) }}</Badge>
            </div>
            <div>
              <h2 class="text-lg font-semibold leading-snug">{{ p.name }}</h2>
              <p class="mt-1 text-sm text-muted-foreground">{{ p.tagline }}</p>
            </div>
            <dl v-for="s in p.sessions" :key="s.id" class="mt-auto space-y-1.5 text-sm">
              <div class="flex items-center gap-2">
                <Calendar class="size-4 text-muted-foreground" />
                <dd>{{ dateRange(s.startDate, s.endDate) }}</dd>
              </div>
              <div class="flex items-center gap-2">
                <MapPin class="size-4 text-muted-foreground" />
                <dd>{{ p.location }}</dd>
              </div>
              <div class="flex items-center justify-between border-t pt-3">
                <span
                  >{{ money(s.priceCents) }}{{ audience(s) === 'Adults' ? '' : ' per camper' }} ·
                  {{ audience(s) }}</span
                >
                <span :class="s.remaining <= 0 ? 'text-destructive' : 'text-muted-foreground'">{{
                  s.remaining <= 0 ? 'Waitlist' : `${s.remaining} open`
                }}</span>
              </div>
            </dl>
          </CardContent>
        </Card>
      </RouterLink>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ArrowLeft, FileText, HandHeart } from '@lucide/vue'
import { computed, onMounted, ref } from 'vue'
import { RouterLink } from 'vue-router'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'
import { api, ApiError } from '@/lib/api'
import { date, dateRange, money } from '@/lib/format'
import StatusBadge from './StatusBadge.vue'
import type { FamilyScholarships } from './types'

// O6 · A family's scholarship applications, and the registrations they can still ask for help with.
const data = ref<FamilyScholarships | null>(null)
const loadError = ref<string | null>(null)
onMounted(async () => {
  try {
    data.value = await api.get<FamilyScholarships>('/family/scholarships')
  } catch (e) {
    loadError.value = e instanceof ApiError ? e.message : "Couldn't load your scholarships."
  }
})
const label = (s: string) => (s === 'Submitted' ? 'In review' : s)
const eligible = computed(() => data.value?.orders.filter((o) => o.canApply) ?? [])
</script>

<template>
  <div class="mx-auto max-w-4xl px-4 py-8 md:py-12">
    <Button variant="link" as-child class="h-auto p-0 text-muted-foreground">
      <RouterLink to="/family"><ArrowLeft />My family</RouterLink>
    </Button>
    <h1 class="mt-4 text-3xl font-semibold tracking-tight md:text-4xl">Scholarships</h1>
    <p class="mt-2 text-muted-foreground">
      Financial assistance is available for families who need help with camp costs. Every request is reviewed by our
      staff, and an award is applied straight to your balance.
    </p>

    <Alert v-if="loadError" variant="destructive" class="mt-6"
      ><AlertDescription>{{ loadError }}</AlertDescription></Alert
    >
    <div v-else-if="!data" class="mt-8 space-y-4">
      <Skeleton class="h-28 rounded-xl" /><Skeleton class="h-28 rounded-xl" />
    </div>

    <template v-else>
      <section class="mt-8 space-y-3">
        <h2 class="text-xl font-semibold">Apply for a registration</h2>
        <p v-if="!eligible.length" class="text-muted-foreground">
          None of your upcoming registrations has a balance to apply toward right now.
        </p>
        <Card v-for="o in eligible" :key="o.confirmationCode">
          <CardContent class="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
            <div class="min-w-0">
              <p class="font-medium">{{ o.program }} · {{ o.session }}</p>
              <p class="text-sm text-muted-foreground">
                {{ dateRange(o.startDate, o.endDate) }} · {{ o.campers.map((c) => c.firstName).join(', ') }}
              </p>
              <p class="text-sm">
                Balance due <span class="font-medium tabular-nums">{{ money(o.balanceCents) }}</span>
              </p>
            </div>
            <Button as-child class="sm:shrink-0">
              <RouterLink :to="`/family/scholarships/apply/${o.confirmationCode}`"
                ><HandHeart />Apply for assistance</RouterLink
              >
            </Button>
          </CardContent>
        </Card>
      </section>

      <section class="mt-10 space-y-3">
        <h2 class="text-xl font-semibold">Your applications</h2>
        <p v-if="!data.applications.length" class="text-muted-foreground">You haven't applied for a scholarship yet.</p>
        <Card v-for="a in data.applications" :key="a.id" :data-testid="`application-${a.id}`">
          <CardHeader>
            <CardTitle class="flex flex-wrap items-center gap-2 text-base">
              {{ a.program }} · {{ a.session }} <StatusBadge :status="label(a.status)" />
            </CardTitle>
            <CardDescription>
              {{ a.campers.join(', ') }} · submitted {{ date(a.submittedAt) }} ·
              <span class="font-mono">{{ a.confirmationCode }}</span>
            </CardDescription>
          </CardHeader>
          <CardContent class="space-y-1 text-sm">
            <p>
              Requested <span class="tabular-nums">{{ money(a.requestedCents) }}</span>
            </p>
            <p v-if="a.status === 'Approved'" class="font-medium text-emerald-700">
              Awarded {{ money(a.awardCents) }}. It has been applied to your balance.
            </p>
            <p v-else-if="a.status === 'Denied'" class="text-muted-foreground">
              We weren't able to offer assistance this time. Contact the camp office with any questions.
            </p>
            <p v-else class="text-muted-foreground">
              We'll email you when a decision is made, usually within 5 business days.
            </p>
            <p v-if="a.documentName" class="flex items-center gap-1 text-muted-foreground">
              <FileText class="size-4" />{{ a.documentName }}
            </p>
          </CardContent>
        </Card>
      </section>
    </template>
  </div>
</template>

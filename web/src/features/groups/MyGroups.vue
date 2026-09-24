<script setup lang="ts">
import { ChevronRight, Users } from '@lucide/vue'
import { onMounted, ref } from 'vue'
import { RouterLink } from 'vue-router'
import StatusBadge from '@/components/StatusBadge.vue'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'
import { api } from '@/lib/api'
import { dateRange } from '@/lib/format'
import type { GroupSummary } from './types'

const groups = ref<GroupSummary[] | null>(null)
onMounted(async () => {
  groups.value = await api.get<GroupSummary[]>('/groups')
})

function link(g: GroupSummary) {
  return g.status === 'Draft' ? `/groups/${g.id}/roster` : `/groups/${g.id}`
}
</script>

<template>
  <div class="mx-auto max-w-4xl px-4 py-8 md:py-12">
    <h1 class="text-3xl font-semibold tracking-tight">Your groups</h1>
    <p class="mt-1 text-muted-foreground">Groups you've registered for cohort programs.</p>

    <div v-if="!groups" class="mt-8 space-y-3">
      <Skeleton class="h-24 rounded-xl" /><Skeleton class="h-24 rounded-xl" />
    </div>

    <Card v-else-if="!groups.length" class="mt-8">
      <CardHeader>
        <CardTitle>No groups yet</CardTitle>
        <CardDescription>
          Leadership cohorts are registered by a group leader. Open a cohort program to register your group.
        </CardDescription>
      </CardHeader>
      <CardContent>
        <Button as-child
          ><RouterLink to="/programs/emerging-leaders-cohort">Emerging Leaders Cohort</RouterLink></Button
        >
      </CardContent>
    </Card>

    <ul v-else class="mt-8 space-y-3">
      <li v-for="g in groups" :key="g.id">
        <RouterLink
          :to="link(g)"
          class="flex items-center gap-4 rounded-xl border bg-card p-4 transition-colors hover:bg-muted/50 focus-visible:ring-2 focus-visible:ring-ring focus-visible:outline-none"
        >
          <span class="flex size-10 shrink-0 items-center justify-center rounded-full bg-muted">
            <Users class="size-5 text-muted-foreground" />
          </span>
          <span class="min-w-0 flex-1">
            <span class="flex flex-wrap items-center gap-2">
              <span class="font-medium">{{ g.name }}</span>
              <StatusBadge :status="g.status" />
            </span>
            <span class="block text-sm text-muted-foreground">
              {{ g.program }} · {{ dateRange(g.startDate, g.endDate) }}
            </span>
            <span class="mt-1 block text-sm">
              <template v-if="g.status === 'Draft'"
                >{{ g.counts.attendees }} {{ g.counts.attendees === 1 ? 'attendee' : 'attendees' }} · not paid
                yet</template
              >
              <template v-else>
                {{ g.counts.attendees }} attendees · {{ g.counts.complete }} complete ·
                {{ g.counts.incomplete }} incomplete
                <span v-if="g.counts.withdrawalRequests" class="font-medium text-amber-700">
                  · {{ g.counts.withdrawalRequests }} withdrawal
                  {{ g.counts.withdrawalRequests === 1 ? 'request' : 'requests' }} to review</span
                >
              </template>
            </span>
          </span>
          <ChevronRight class="size-5 shrink-0 text-muted-foreground" />
        </RouterLink>
      </li>
    </ul>
  </div>
</template>

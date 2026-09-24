<script setup lang="ts">
import { ArrowRight } from '@lucide/vue'
import { onMounted, ref } from 'vue'
import { RouterLink } from 'vue-router'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'
import { api } from '@/lib/api'
import { dateTime, money } from '@/lib/format'
import type { FamilyTransferRow, TransferStatus } from './types'

// F8 · Where a family starts a transfer request and follows up on it.
interface Data {
  registrations: {
    id: number
    participant: string
    program: string
    session: string
    dates: string
    pool: string
    hasPendingRequest: boolean
    canRequest: boolean
  }[]
  requests: FamilyTransferRow[]
}
const data = ref<Data | null>(null)
onMounted(async () => {
  data.value = await api.get<Data>('/transfers')
})
const tone: Record<TransferStatus, string> = {
  Pending: 'border-amber-200 bg-amber-50 text-amber-800',
  Approved: 'border-emerald-200 bg-emerald-50 text-emerald-800',
  Denied: 'border-red-200 bg-red-50 text-red-800',
}
const statusText: Record<TransferStatus, string> = {
  Pending: 'Waiting for review',
  Approved: 'Approved',
  Denied: 'Denied',
}
</script>

<template>
  <div class="mx-auto max-w-4xl space-y-8 px-4 py-8 md:py-12">
    <div>
      <h1 class="text-3xl font-semibold tracking-tight">Session transfers</h1>
      <p class="mt-1 text-muted-foreground">
        Need a different week? Ask to move a confirmed camper to another session of the same program. Your current spot
        stays confirmed until staff approve the move.
      </p>
    </div>

    <div v-if="!data" class="space-y-4"><Skeleton class="h-32 rounded-xl" /><Skeleton class="h-32 rounded-xl" /></div>
    <template v-else>
      <Card>
        <CardHeader>
          <CardTitle>Confirmed registrations</CardTitle>
          <CardDescription>Pick the camper you'd like to move.</CardDescription>
        </CardHeader>
        <CardContent>
          <p v-if="!data.registrations.length" class="text-sm text-muted-foreground">
            No confirmed registrations yet. Once a camper is confirmed, you can ask to move them here.
          </p>
          <ul class="divide-y">
            <li
              v-for="r in data.registrations"
              :key="r.id"
              class="flex flex-wrap items-center gap-3 py-3 first:pt-0 last:pb-0"
            >
              <div class="min-w-0 flex-1">
                <div class="font-medium">{{ r.participant }}</div>
                <div class="text-sm text-muted-foreground">{{ r.program }} · {{ r.dates }} · {{ r.pool }}</div>
              </div>
              <Badge v-if="r.hasPendingRequest" variant="outline" :class="tone.Pending">Transfer requested</Badge>
              <Button v-else-if="r.canRequest" variant="outline" size="sm" as-child>
                <RouterLink :to="`/family/registrations/${r.id}/transfer`">Request transfer <ArrowRight /></RouterLink>
              </Button>
              <span v-else class="text-sm text-muted-foreground">No other sessions to move to</span>
            </li>
          </ul>
        </CardContent>
      </Card>

      <Card>
        <CardHeader><CardTitle>Your requests</CardTitle></CardHeader>
        <CardContent>
          <p v-if="!data.requests.length" class="text-sm text-muted-foreground">
            You haven't asked for a transfer yet.
          </p>
          <ul class="space-y-3">
            <li v-for="t in data.requests" :key="t.id" class="rounded-lg border p-4">
              <div class="flex flex-wrap items-center justify-between gap-2">
                <div class="font-medium">{{ t.participant }}: {{ t.from }} → {{ t.to }}</div>
                <Badge variant="outline" :class="tone[t.status]">{{ statusText[t.status] }}</Badge>
              </div>
              <p class="text-sm text-muted-foreground">{{ t.program }} · asked {{ dateTime(t.createdAt) }}</p>
              <p v-if="t.status === 'Denied'" class="mt-2 text-sm">
                <span class="font-medium">Reason:</span> {{ t.decisionNote }} Your {{ t.from }} registration is still
                confirmed.
              </p>
              <p v-else-if="t.status === 'Approved'" class="mt-2 text-sm">
                Moved to {{ t.to }}.<template v-if="t.refundCents">
                  {{ money(t.refundCents) }} was refunded to your card.</template
                >
              </p>
              <p v-else class="mt-2 text-sm">Staff will review it. Nothing changes until they approve.</p>
            </li>
          </ul>
        </CardContent>
      </Card>
    </template>
  </div>
</template>

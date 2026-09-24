<script setup lang="ts">
import { CircleCheck } from '@lucide/vue'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { cn } from '@/lib/utils'
import { date, dateTime } from '@/lib/format'
import PublishBadge from './PublishBadge.vue'
import type { WaiverDetail } from './types'

// K7 right rail: every version with who approved it and how many families signed it, then recent signatures.
defineProps<{ waiver: WaiverDetail }>()
const dot: Record<string, string> = {
  Draft: 'bg-sky-500',
  PendingApproval: 'bg-amber-500',
  Published: 'bg-emerald-600',
  Archived: 'bg-muted-foreground/50',
}
</script>

<template>
  <div class="space-y-6">
    <Card>
      <CardHeader><CardTitle>Version history</CardTitle></CardHeader>
      <CardContent>
        <ol class="space-y-5" aria-label="Version history">
          <li v-for="v in waiver.versions" :key="v.id" class="flex gap-3">
            <span :class="cn('mt-1.5 size-2.5 shrink-0 rounded-full', dot[v.status])" aria-hidden="true" />
            <div class="min-w-0 text-sm">
              <p class="flex flex-wrap items-center gap-2 font-medium">
                v{{ v.version }} <PublishBadge :state="v.status" :label="v.statusLabel" />
              </p>
              <p v-if="v.status === 'Published'" class="text-muted-foreground">
                Live since {{ date(v.effectiveDate) }}
              </p>
              <p v-else-if="v.status === 'Archived'" class="text-muted-foreground">
                {{ date(v.effectiveDate) }} – {{ date(v.retiredDate) }}
              </p>
              <p v-if="v.approvedBy" class="text-muted-foreground">
                Approved by {{ v.approvedBy }}<template v-if="v.approvedAt">, {{ dateTime(v.approvedAt) }}</template>
              </p>
              <p v-else-if="v.submittedBy" class="text-muted-foreground">Sent by {{ v.submittedBy }}</p>
              <p v-else class="text-muted-foreground">Drafted by {{ v.createdBy }}</p>
              <p v-if="v.changeNote">{{ v.changeNote }}</p>
              <p v-if="v.status === 'Published' || v.status === 'Archived'" class="text-muted-foreground">
                {{ v.signatures }} {{ v.signatures === 1 ? 'family signed' : 'families signed' }} this version
              </p>
            </div>
          </li>
        </ol>
      </CardContent>
    </Card>

    <Card>
      <CardHeader><CardTitle>Recent signatures</CardTitle></CardHeader>
      <CardContent>
        <p v-if="!waiver.recent.length" class="text-sm text-muted-foreground">No one has signed this waiver yet.</p>
        <ul v-else class="space-y-3">
          <li v-for="a in waiver.recent" :key="a.id" class="flex gap-3 text-sm">
            <CircleCheck class="mt-0.5 size-4 shrink-0 text-emerald-600" aria-hidden="true" />
            <div>
              <p>{{ a.signerName }} signed v{{ a.version }} for {{ a.camper }}</p>
              <p class="text-muted-foreground">{{ a.session }} · {{ dateTime(a.acceptedAt) }}</p>
            </div>
          </li>
        </ul>
      </CardContent>
    </Card>
  </div>
</template>

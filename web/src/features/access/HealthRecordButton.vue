<script setup lang="ts">
import { ExternalLink, HeartPulse, Lock } from '@lucide/vue'
import { ref } from 'vue'
import StatusBadge from '@/components/StatusBadge.vue'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { Skeleton } from '@/components/ui/skeleton'
import { api, ApiError } from '@/lib/api'
import type { HealthRecord } from './types'

// Health details for one registration, behind the server's four-check rule (FR-112). Each open is audited,
// so nothing is prefetched: the request happens only when staff ask to see the form.
const props = defineProps<{ registrationId: number }>()

const open = ref(false)
const record = ref<HealthRecord | null>(null)
const refusal = ref<string | null>(null)
const failed = ref<string | null>(null)

async function show() {
  open.value = true
  record.value = null
  refusal.value = null
  failed.value = null
  try {
    record.value = await api.get<HealthRecord>(`/access/registrations/${props.registrationId}/health`)
  } catch (e) {
    if (e instanceof ApiError && e.status === 403) refusal.value = e.message
    else failed.value = e instanceof ApiError ? e.message : "Couldn't load the health form. Try again."
  }
}

const rows: { key: keyof NonNullable<HealthRecord['details']>; label: string }[] = [
  { key: 'allergies', label: 'Allergies' },
  { key: 'medications', label: 'Medications' },
  { key: 'dietary', label: 'Dietary needs' },
  { key: 'adaNeeds', label: 'Accessibility needs' },
  { key: 'physicianName', label: 'Physician' },
  { key: 'physicianPhone', label: 'Physician phone' },
  { key: 'insuranceProvider', label: 'Insurance' },
]
</script>

<template>
  <div>
    <Button variant="outline" size="sm" @click="show"><HeartPulse />View health form</Button>
    <Dialog v-model:open="open">
      <DialogContent class="max-h-[90dvh] overflow-y-auto sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>{{ record ? `${record.camper}'s health form` : 'Health form' }}</DialogTitle>
          <DialogDescription v-if="record" class="flex flex-wrap items-center gap-2">
            {{ record.program }} <StatusBadge :status="record.status" />
          </DialogDescription>
          <DialogDescription v-else>Health details are shown only to staff a program allows.</DialogDescription>
        </DialogHeader>

        <Alert v-if="refusal" data-testid="health-refused">
          <Lock />
          <AlertTitle class="line-clamp-none">You can't view this health form</AlertTitle>
          <AlertDescription>
            <p>{{ refusal }}</p>
            <p class="mt-1">This attempt was recorded in the audit log.</p>
          </AlertDescription>
        </Alert>
        <Alert v-else-if="failed" variant="destructive"
          ><AlertDescription>{{ failed }}</AlertDescription></Alert
        >
        <div v-else-if="!record" class="space-y-3" aria-busy="true">
          <Skeleton v-for="i in 4" :key="i" class="h-10 w-full" />
        </div>
        <template v-else-if="record.details">
          <dl class="divide-y text-sm">
            <div v-for="row in rows" :key="row.key" class="grid gap-1 py-2 sm:grid-cols-[10rem_1fr] sm:gap-4">
              <dt class="text-muted-foreground">{{ row.label }}</dt>
              <dd>{{ record.details[row.key] ?? 'None given' }}</dd>
            </div>
          </dl>
          <p class="flex items-center gap-1 text-sm text-muted-foreground">
            <Lock class="size-3.5" />Your view was recorded in the audit log.
          </p>
        </template>
        <Alert v-else>
          <AlertDescription>
            <p>{{ record.message }}</p>
            <Button
              v-if="record.link"
              as="a"
              :href="record.link"
              target="_blank"
              rel="noopener"
              variant="outline"
              size="sm"
              class="mt-2"
            >
              <ExternalLink />{{ record.mechanism === 'CampDoc' ? 'Open CampDoc' : 'Open the form' }}
            </Button>
          </AlertDescription>
        </Alert>

        <DialogFooter>
          <Button variant="outline" @click="open = false">Close</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  </div>
</template>

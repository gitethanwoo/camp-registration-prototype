<script setup lang="ts">
import { CircleX, LogIn, LogOut, Phone, TriangleAlert } from '@lucide/vue'
import { computed, ref, watch } from 'vue'
import { toast } from 'vue-sonner'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import {
  AlertDialog,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from '@/components/ui/alert-dialog'
import { Avatar, AvatarFallback } from '@/components/ui/avatar'
import { Button } from '@/components/ui/button'
import { Checkbox } from '@/components/ui/checkbox'
import { Label } from '@/components/ui/label'
import { RadioGroup, RadioGroupItem } from '@/components/ui/radio-group'
import { Separator } from '@/components/ui/separator'
import { Textarea } from '@/components/ui/textarea'
import { api } from '@/lib/api'
import { dateTime, initials } from '@/lib/format'
import CheckInStatusBadge from './CheckInStatusBadge.vue'
import type { CheckInRow } from './types'
import { genderLabel } from './types'
import { describe } from './useOpsData'

// O5 · One camper's check-in or check-out. Blocked campers need a written override; pickup needs an
// authorized adult whose photo ID staff checked.
const props = defineProps<{ row: CheckInRow; canEdit: boolean }>()
const emit = defineEmits<{ changed: [] }>()

const busy = ref(false)
const overrideOpen = ref(false)
const reason = ref('')
const reasonError = ref<string | null>(null)
const adultId = ref<string>('')
const idChecked = ref(false)
const checkoutError = ref<string | null>(null)
watch(
  () => props.row.registrationId,
  () => {
    reason.value = ''
    reasonError.value = null
    adultId.value = ''
    idChecked.value = false
    checkoutError.value = null
  },
)
const blockedSummary = computed(() => props.row.blockers.map((b) => b.label).join(' · '))

async function checkIn(overrideReason: string | null) {
  busy.value = true
  try {
    await api.post(`/admin/ops/check-in/${props.row.registrationId}`, { overrideReason })
    toast.success(`${props.row.name} is checked in.`, {
      description: overrideReason ? 'Override and reason saved to the audit log.' : undefined,
    })
    overrideOpen.value = false
    emit('changed')
  } catch (e) {
    toast.error(describe(e, "Check-in wasn't saved. Try again."))
  } finally {
    busy.value = false
  }
}
function confirmOverride() {
  if (!reason.value.trim()) {
    reasonError.value = 'Say why this camper is being checked in with open items.'
    return
  }
  checkIn(reason.value.trim())
}

async function checkOut() {
  if (!adultId.value) {
    checkoutError.value = 'Choose who is picking up.'
    return
  }
  if (!idChecked.value) {
    checkoutError.value = 'Confirm you checked their photo ID.'
    return
  }
  busy.value = true
  try {
    await api.post(`/admin/ops/check-out/${props.row.registrationId}`, {
      pickupAdultId: Number(adultId.value),
      idChecked: idChecked.value,
    })
    toast.success(`${props.row.name} is checked out.`)
    emit('changed')
  } catch (e) {
    checkoutError.value = describe(e, "Check-out wasn't saved. Try again.")
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <div class="space-y-5" data-testid="check-in-panel">
    <div class="flex items-start gap-4">
      <Avatar class="size-14"
        ><AvatarFallback class="text-lg">{{ initials(row.name) }}</AvatarFallback></Avatar
      >
      <div class="min-w-0">
        <h2 class="text-xl font-semibold">{{ row.name }}</h2>
        <p class="text-sm text-muted-foreground">
          Grade {{ row.grade }} · {{ genderLabel(row.gender) }} · {{ row.cabin }}
        </p>
        <p class="text-sm text-muted-foreground">Confirmation {{ row.confirmationCode }}</p>
        <CheckInStatusBadge class="mt-2" :status="row.status" />
      </div>
    </div>
    <Separator />

    <template v-if="row.status === 'Blocked'">
      <Alert variant="destructive">
        <CircleX class="size-4" />
        <AlertTitle>Check-in blocked</AlertTitle>
        <AlertDescription>{{ blockedSummary }}: resolve before check-in.</AlertDescription>
      </Alert>
      <ul class="space-y-2">
        <li v-for="b in row.blockers" :key="b.kind" class="flex gap-3 rounded-md border p-3">
          <TriangleAlert class="mt-0.5 size-4 shrink-0 text-amber-600" aria-hidden="true" />
          <div>
            <p class="text-sm font-medium">{{ b.label }}</p>
            <p class="text-sm text-muted-foreground">{{ b.detail }}</p>
          </div>
        </li>
      </ul>
      <Button class="h-12 w-full" size="lg" disabled><LogIn class="size-4" />Confirm check-in</Button>
      <Button v-if="canEdit" variant="outline" class="h-11 w-full" :disabled="busy" @click="overrideOpen = true">
        Check in anyway with a reason
      </Button>
    </template>

    <Button
      v-else-if="row.status === 'Ready' && canEdit"
      class="h-12 w-full"
      size="lg"
      :disabled="busy"
      @click="checkIn(null)"
    >
      <LogIn class="size-4" />Confirm check-in
    </Button>
    <p v-else-if="row.status === 'Ready'" class="text-sm text-muted-foreground">Ready for check-in.</p>

    <template v-if="row.status === 'Checked in'">
      <p class="text-sm text-muted-foreground">
        Checked in {{ row.checkedInAt ? dateTime(row.checkedInAt) : '' }} by {{ row.checkedInBy }}.
      </p>
      <p v-if="row.checkInOverride" class="rounded-md border border-amber-300 bg-amber-50 p-3 text-sm text-amber-900">
        Checked in with open items. Reason: {{ row.checkInOverride }}
      </p>
      <section v-if="canEdit" class="space-y-3" aria-labelledby="checkout-heading">
        <h3 id="checkout-heading" class="font-semibold">Check-out</h3>
        <p class="text-sm text-muted-foreground">Release {{ row.name }} only to an adult on this list.</p>
        <RadioGroup v-model="adultId" aria-label="Authorized pickup adult" class="gap-2">
          <Label
            v-for="a in row.pickupAdults"
            :key="a.id"
            class="flex cursor-pointer items-center gap-3 rounded-md border p-3 font-normal has-[[data-state=checked]]:border-primary"
          >
            <RadioGroupItem :value="String(a.id)" />
            <span
              ><span class="font-medium">{{ a.name }}</span> · {{ a.relationship }}</span
            >
          </Label>
        </RadioGroup>
        <Label class="flex items-center gap-3 font-normal">
          <Checkbox v-model="idChecked" />I checked this adult's photo ID
        </Label>
        <p v-if="row.guardianPhone" class="flex items-center gap-2 text-sm text-muted-foreground">
          <Phone class="size-4" aria-hidden="true" />Not on the list? Don't release the camper. Call the guardian at
          {{ row.guardianPhone }}.
        </p>
        <p v-if="checkoutError" class="text-sm text-destructive" role="alert">{{ checkoutError }}</p>
        <Button class="h-12 w-full" size="lg" :disabled="busy" @click="checkOut"
          ><LogOut class="size-4" />Confirm check-out</Button
        >
      </section>
    </template>

    <p v-if="row.status === 'Checked out'" class="text-sm text-muted-foreground">
      Picked up by {{ row.pickedUpBy }} {{ row.checkedOutAt ? dateTime(row.checkedOutAt) : '' }}. Released by
      {{ row.checkedOutBy }}.
    </p>

    <AlertDialog v-model:open="overrideOpen">
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>Check in {{ row.name }} with open items?</AlertDialogTitle>
          <AlertDialogDescription>
            Still open: {{ blockedSummary }}. Your name and reason go in the audit log.
          </AlertDialogDescription>
        </AlertDialogHeader>
        <div class="space-y-2">
          <Label for="override-reason">Reason</Label>
          <Textarea
            id="override-reason"
            v-model="reason"
            maxlength="500"
            placeholder="For example: parent paid the balance at the table, receipt 4471"
            :aria-invalid="!!reasonError || undefined"
          />
          <p v-if="reasonError" class="text-sm text-destructive" role="alert">{{ reasonError }}</p>
        </div>
        <AlertDialogFooter>
          <AlertDialogCancel>Cancel</AlertDialogCancel>
          <Button :disabled="busy" @click="confirmOverride">Check in with override</Button>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  </div>
</template>

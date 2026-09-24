<script setup lang="ts">
import { ArrowLeft, CircleCheck, Info, TriangleAlert, XCircle } from '@lucide/vue'
import { computed, onMounted, ref } from 'vue'
import { RouterLink } from 'vue-router'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Skeleton } from '@/components/ui/skeleton'
import { Textarea } from '@/components/ui/textarea'
import { api, ApiError } from '@/lib/api'
import { dateTime, money } from '@/lib/format'
import type { FamilyTransferRow, TransferCheck } from './types'

// F8 · A request, not a move: the current registration stays confirmed until staff approve (FR-42).
const props = defineProps<{ id: number }>()
interface Options {
  registration: {
    id: number
    participant: string
    firstName: string
    program: string
    session: string
    dates: string
    pool: string
    status: string
    priceCents: number
    paidCents: number
    balanceCents: number
  }
  pendingRequest: FamilyTransferRow | null
  options: TransferCheck[]
}
const data = ref<Options | null>(null)
const loadError = ref<string | null>(null)
const toSessionId = ref<string>('')
const reason = ref('')
const busy = ref(false)
const error = ref<string | null>(null)
const submitted = ref(false)

async function load() {
  try {
    data.value = await api.get<Options>(`/transfers/${props.id}/options`)
    const first = data.value.options.find((o) => o.canMove)
    if (first && !toSessionId.value) toSessionId.value = String(first.sessionId)
  } catch (e) {
    loadError.value =
      e instanceof ApiError && e.status === 404
        ? "We couldn't find that registration on your account."
        : "Couldn't load your sessions."
  }
}
onMounted(load)

const choice = computed(() => data.value?.options.find((o) => String(o.sessionId) === toSessionId.value) ?? null)
const optionLabel = (o: TransferCheck) =>
  `${o.session.split(' · ')[1] ?? o.session} — ${o.pool ?? 'no matching group'} (${o.canMove ? `${o.spotsLeft} ${o.spotsLeft === 1 ? 'spot' : 'spots'} left` : 'not available'})`

async function submit() {
  if (!choice.value) {
    error.value = 'Choose the session you want to move to.'
    return
  }
  if (!reason.value.trim()) {
    error.value = "Tell us why you'd like to switch sessions."
    return
  }
  busy.value = true
  error.value = null
  try {
    await api.post('/transfers', {
      registrationId: props.id,
      toSessionId: choice.value.sessionId,
      reason: reason.value.trim(),
    })
    submitted.value = true
    await load()
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : "Your request didn't go through. Try again."
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <div class="mx-auto max-w-4xl space-y-6 px-4 py-8 md:py-12">
    <Button variant="ghost" size="sm" as-child class="-ml-2.5 text-muted-foreground">
      <RouterLink to="/family/transfers"><ArrowLeft />Session transfers</RouterLink>
    </Button>
    <div>
      <h1 class="text-3xl font-semibold tracking-tight">Request a session transfer</h1>
      <p class="mt-1 text-muted-foreground">
        Move to a different session of the same camp. Staff review every request, and your current registration stays
        confirmed until they approve it.
      </p>
    </div>

    <Alert v-if="loadError" variant="destructive"
      ><AlertDescription>{{ loadError }}</AlertDescription></Alert
    >
    <Skeleton v-else-if="!data" class="h-96 rounded-xl" />
    <template v-else>
      <Alert v-if="data.pendingRequest" role="status">
        <CircleCheck />
        <AlertTitle class="line-clamp-none">{{ submitted ? 'Request sent' : 'Request waiting for review' }}</AlertTitle>
        <AlertDescription>
          <p>
            You asked to move {{ data.registration.firstName }} from {{ data.pendingRequest.from }} to
            {{ data.pendingRequest.to }} on {{ dateTime(data.pendingRequest.createdAt) }}. Staff will review it, and
            {{ data.registration.firstName }} stays in {{ data.registration.dates }} until they approve.
          </p>
          <RouterLink to="/family/transfers" class="font-medium underline">See all your requests</RouterLink>
        </AlertDescription>
      </Alert>

      <Card v-else>
        <CardContent class="space-y-6">
          <Alert class="border-amber-200 bg-amber-50 text-amber-900">
            <Info />
            <AlertTitle class="line-clamp-none">This is a request, not an immediate transfer</AlertTitle>
            <AlertDescription class="text-amber-900/90"
              >Staff will review it. Your current registration stays confirmed until approval.</AlertDescription
            >
          </Alert>

          <section class="space-y-2">
            <h2 class="font-semibold">Current session</h2>
            <div class="rounded-lg bg-muted/50 p-4 text-sm">
              <p class="font-medium">{{ data.registration.participant }}</p>
              <p>{{ data.registration.program }} · {{ data.registration.pool }}</p>
              <p>{{ data.registration.dates }}</p>
              <p class="tabular-nums">{{ money(data.registration.priceCents) }}</p>
            </div>
          </section>

          <form class="space-y-6" @submit.prevent="submit">
            <section class="space-y-2">
              <Label for="to-session" class="text-base font-semibold">Select new session</Label>
              <p v-if="!data.options.length" class="text-sm text-muted-foreground">
                This program has no other sessions to move to.
              </p>
              <Select v-else v-model="toSessionId">
                <SelectTrigger id="to-session" class="w-full"
                  ><SelectValue placeholder="Choose a session"
                /></SelectTrigger>
                <SelectContent>
                  <SelectItem
                    v-for="o in data.options"
                    :key="o.sessionId"
                    :value="String(o.sessionId)"
                    :disabled="!o.canMove"
                    >{{ optionLabel(o) }}</SelectItem
                  >
                </SelectContent>
              </Select>
              <div v-if="choice" class="rounded-lg bg-muted/50 p-4 text-sm">
                <p class="font-medium">{{ data.registration.program }}</p>
                <p>{{ choice.session.split(' · ')[1] ?? choice.session }}</p>
                <p>
                  {{ choice.pool ?? 'No matching group' }} ·
                  <span :class="choice.canMove ? 'font-medium text-emerald-700' : 'font-medium text-destructive'">{{
                    choice.canMove
                      ? `${choice.spotsLeft} ${choice.spotsLeft === 1 ? 'spot' : 'spots'} left`
                      : 'No availability'
                  }}</span>
                </p>
                <p class="tabular-nums">{{ money(choice.priceCents) }}</p>
              </div>
              <ul v-if="choice" class="space-y-1 text-sm">
                <li v-for="r in choice.requirements" :key="r.label" class="flex gap-2">
                  <CircleCheck v-if="r.state === 'Ok'" class="mt-0.5 size-4 shrink-0 text-emerald-700" />
                  <TriangleAlert v-else-if="r.state === 'Review'" class="mt-0.5 size-4 shrink-0 text-amber-600" />
                  <XCircle v-else class="mt-0.5 size-4 shrink-0 text-destructive" />
                  <span
                    ><span class="font-medium">{{ r.label }}:</span> {{ r.detail }}</span
                  >
                </li>
                <li v-for="b in choice.blockers" :key="b" class="flex gap-2 text-destructive">
                  <XCircle class="mt-0.5 size-4 shrink-0" /><span>{{ b }}</span>
                </li>
              </ul>
            </section>

            <section v-if="choice" class="space-y-2">
              <h2 class="font-semibold">Price difference</h2>
              <p class="rounded-lg bg-muted/50 p-3 text-sm">
                <span class="font-medium tabular-nums">{{
                  choice.priceDifferenceCents >= 0
                    ? money(choice.priceDifferenceCents)
                    : `−${money(-choice.priceDifferenceCents)}`
                }}</span>
                <span class="ml-2 text-muted-foreground">{{
                  choice.priceDifferenceCents === 0
                    ? 'You will not be charged.'
                    : choice.refundCents
                      ? `If approved, ${money(choice.refundCents)} goes back to your card.`
                      : choice.priceDifferenceCents > 0
                        ? `If approved, it's added to your balance (${money(choice.newBalanceCents)} total).`
                        : `If approved, your balance drops to ${money(choice.newBalanceCents)}.`
                }}</span>
              </p>
            </section>

            <section class="space-y-2">
              <Label for="reason" class="text-base font-semibold">Reason for transfer request</Label>
              <Textarea
                id="reason"
                v-model="reason"
                rows="3"
                maxlength="500"
                placeholder="e.g. We'll be out of town the first week."
              />
              <p class="text-right text-xs text-muted-foreground tabular-nums">{{ reason.length }}/500</p>
            </section>

            <p v-if="error" class="text-sm text-destructive" role="alert">{{ error }}</p>
            <Button type="submit" :disabled="busy || !choice?.canMove">{{
              busy ? 'Sending…' : 'Submit transfer request'
            }}</Button>
          </form>
        </CardContent>
      </Card>
    </template>
  </div>
</template>

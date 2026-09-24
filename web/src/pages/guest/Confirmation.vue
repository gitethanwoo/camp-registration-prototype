<script setup lang="ts">
import { CircleCheck, Clock, Mail, MailCheck } from '@lucide/vue'
import { onMounted, onUnmounted, ref } from 'vue'
import { RouterLink } from 'vue-router'
import StatusBadge from '@/components/StatusBadge.vue'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Separator } from '@/components/ui/separator'
import { Skeleton } from '@/components/ui/skeleton'
import { api } from '@/lib/api'
import { date, dateRange, money } from '@/lib/format'

interface Order {
  confirmationCode: string
  status: 'Pending' | 'Paid' | 'Declined' | 'Waitlisted'
  email: string
  emailSent: boolean
  program: { name: string; slug: string; location: string; healthMechanism: string }
  session: { id: number; name: string; startDate: string; endDate: string }
  registrations: { id: number; firstName: string; lastName: string; pool: string; status: string }[]
  waitlisted: { id: number; firstName: string; pool: string; position: number; status: string }[]
  payment: {
    option: string
    totalCents: number
    discountCents: number
    chargedCents: number
    cardLast4: string | null
    balanceCents: number
    balanceDueDate: string
    installments: { dueDate: string; amountCents: number; status: string }[]
  }
}

const props = defineProps<{ code: string }>()
const order = ref<Order | null>(null)
let timer: number | undefined

async function load() {
  order.value = await api.get<Order>(`/orders/${props.code}`)
  // The confirmation email goes out through the outbox a few seconds later; reflect that here.
  if (!order.value.emailSent) timer = window.setTimeout(load, 2000)
}
onMounted(load)
onUnmounted(() => clearTimeout(timer))
</script>

<template>
  <div class="mx-auto max-w-2xl px-4 py-8 md:py-12">
    <Skeleton v-if="!order" class="h-96 rounded-xl" />
    <template v-else>
      <div class="text-center">
        <div
          :class="[
            'mx-auto flex size-14 items-center justify-center rounded-full',
            order.registrations.length ? 'bg-emerald-100 text-emerald-700' : 'bg-violet-100 text-violet-700',
          ]"
        >
          <CircleCheck v-if="order.registrations.length" class="size-8" />
          <Clock v-else class="size-8" />
        </div>
        <h1 class="mt-4 text-2xl font-semibold tracking-tight md:text-3xl">
          {{ order.registrations.length ? 'You’re registered!' : 'You’re on the waitlist' }}
        </h1>
        <p class="mt-2 text-muted-foreground">
          {{ order.program.name }} · {{ order.session.name }} ·
          {{ dateRange(order.session.startDate, order.session.endDate) }}
        </p>
        <p class="mt-3 text-sm">
          Confirmation <span class="font-mono font-semibold">{{ order.confirmationCode }}</span>
        </p>
        <p class="mt-1 inline-flex items-center gap-1.5 text-sm text-muted-foreground" aria-live="polite">
          <MailCheck v-if="order.emailSent" class="size-4 text-emerald-700" /><Mail v-else class="size-4" />
          {{ order.emailSent ? `Confirmation emailed to ${order.email}` : `Sending confirmation to ${order.email}…` }}
        </p>
      </div>

      <Card class="mt-8">
        <CardHeader><CardTitle>Participants</CardTitle></CardHeader>
        <CardContent class="divide-y text-sm">
          <div
            v-for="r in order.registrations"
            :key="r.id"
            class="flex items-center justify-between py-3 first:pt-0 last:pb-0"
          >
            <span
              ><span class="font-medium">{{ r.firstName }} {{ r.lastName }}</span>
              <span class="text-muted-foreground">· {{ r.pool }}</span></span
            >
            <StatusBadge :status="r.status" />
          </div>
          <div v-for="w in order.waitlisted" :key="`w${w.id}`" class="py-3 first:pt-0 last:pb-0">
            <div class="flex items-center justify-between">
              <span
                ><span class="font-medium">{{ w.firstName }}</span>
                <span class="text-muted-foreground">· {{ w.pool }}</span></span
              >
              <StatusBadge status="Waitlisted" :label="`Waitlist #${w.position}`" />
            </div>
            <p class="mt-1 text-muted-foreground">
              {{ w.pool }} filled before checkout finished. {{ w.firstName }} is #{{ w.position }} in line and wasn't
              charged. If a spot opens, we'll email you an offer with a deadline to accept.
            </p>
          </div>
        </CardContent>
      </Card>

      <Card v-if="order.registrations.length" class="mt-4">
        <CardHeader>
          <CardTitle>Payment</CardTitle>
          <CardDescription v-if="order.payment.cardLast4"
            >Charged to card ending {{ order.payment.cardLast4 }}</CardDescription
          >
        </CardHeader>
        <CardContent class="space-y-2 text-sm">
          <div class="flex justify-between">
            <span>Total</span><span class="tabular-nums">{{ money(order.payment.totalCents) }}</span>
          </div>
          <div class="flex justify-between font-medium">
            <span>Paid today</span><span class="tabular-nums">{{ money(order.payment.chargedCents) }}</span>
          </div>
          <template v-if="order.payment.balanceCents > 0">
            <Separator />
            <template v-if="order.payment.installments.length">
              <p class="font-medium">Payment plan</p>
              <div
                v-for="(i, n) in order.payment.installments"
                :key="n"
                class="flex justify-between text-muted-foreground"
              >
                <span>{{ date(i.dueDate) }}</span
                ><span class="tabular-nums">{{ money(i.amountCents) }}</span>
              </div>
            </template>
            <div v-else class="flex justify-between text-muted-foreground">
              <span>Balance due by {{ date(order.payment.balanceDueDate) }}</span
              ><span class="tabular-nums">{{ money(order.payment.balanceCents) }}</span>
            </div>
          </template>
        </CardContent>
      </Card>

      <Card v-if="order.registrations.length" class="mt-4">
        <CardHeader>
          <CardTitle>What's next</CardTitle>
        </CardHeader>
        <CardContent class="space-y-2 text-sm">
          <p v-if="order.program.healthMechanism === 'CampDoc'">
            Complete each camper's health forms in CampDoc. We've emailed the link, and it's on your checklist.
          </p>
          <p v-else>
            Health forms and waivers are done. We'll email packing lists and drop-off details two weeks before camp.
          </p>
        </CardContent>
      </Card>

      <div class="mt-8 flex flex-col gap-3 sm:flex-row sm:justify-center">
        <Button as-child size="lg"><RouterLink to="/family#checklist">Go to my family checklist</RouterLink></Button>
        <Button as-child size="lg" variant="outline"
          ><RouterLink to="/programs">Browse more programs</RouterLink></Button
        >
      </div>
    </template>
  </div>
</template>

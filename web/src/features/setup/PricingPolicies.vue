<script setup lang="ts">
import { CircleCheck, Plus, Trash2, TriangleAlert } from '@lucide/vue'
import { computed, onMounted, reactive, ref, watch } from 'vue'
import { toast } from 'vue-sonner'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Skeleton } from '@/components/ui/skeleton'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { useAdminScope } from '@/composables/useAdminScope'
import { api } from '@/lib/api'
import { date, money } from '@/lib/format'
import AdminOnly from './AdminOnly.vue'
import type { PricingInput, PricingPage, PricingPreview, RefundBasis } from './types'
import { saveError, toCents, toDollars, useSetupLoad } from './useSetupLoad'

// K4 · Pricing and policies for the scoped session (FR-47, FR-48, FR-49). The server does every sum;
// this page sends what's typed and shows what comes back.
const scope = useAdminScope()
const url = () => (scope.sessionId.value ? `/admin/setup/sessions/${scope.sessionId.value}/pricing` : null)
const { data, forbidden, error, load } = useSetupLoad<PricingPage>(url)
onMounted(async () => {
  await scope.ready
  await load()
})
watch(scope.sessionId, load)

interface TierRow {
  daysBefore: string
  refundPercent: string
  basis: RefundBasis
  adminFee: string
}
const form = reactive({ price: '', deposit: '', planInstallments: '0', balanceDueDate: '', tiers: [] as TierRow[] })
const preview = ref<PricingPreview | null>(null)

function reset() {
  const saved = data.value?.saved
  if (!saved) return
  Object.assign(form, {
    price: toDollars(saved.priceCents),
    deposit: toDollars(saved.depositCents),
    planInstallments: String(saved.planInstallments),
    balanceDueDate: saved.balanceDueDate,
    tiers: saved.tiers.map((t) => ({
      daysBefore: String(t.daysBefore),
      refundPercent: String(t.refundPercent),
      basis: t.basis,
      adminFee: toDollars(t.adminFeeCents),
    })),
  })
  preview.value = data.value?.preview ?? null
}
watch(data, reset)

const body = computed<PricingInput>(() => ({
  priceCents: toCents(form.price) || 0,
  depositCents: toCents(form.deposit) || 0,
  planInstallments: Number(form.planInstallments),
  balanceDueDate: form.balanceDueDate,
  tiers: form.tiers.map((t) => ({
    daysBefore: Number(t.daysBefore),
    refundPercent: Number(t.refundPercent),
    basis: t.basis,
    adminFeeCents: toCents(t.adminFee) || 0,
  })),
}))
const dirty = computed(() => !!data.value && JSON.stringify(body.value) !== JSON.stringify(data.value.saved))

// Recompute the preview on the server a moment after typing stops.
let timer: ReturnType<typeof setTimeout> | undefined
watch(
  body,
  () => {
    const id = data.value?.session.id
    if (!id || !form.balanceDueDate) return
    clearTimeout(timer)
    timer = setTimeout(async () => {
      try {
        preview.value = await api.post<PricingPreview>(`/admin/setup/sessions/${id}/pricing/preview`, body.value)
      } catch {
        /* the save button reports problems */
      }
    }, 250)
  },
  { deep: true },
)
const problems = computed(() => Object.values(preview.value?.errors ?? {}).flat())
const err = (k: string) => preview.value?.errors[k]?.[0]

function addTier() {
  form.tiers.push({ daysBefore: '', refundPercent: '0', basis: 'AmountPaid', adminFee: '0' })
}
const saving = ref(false)
const saveMessage = ref<string | null>(null)
async function save() {
  const s = data.value?.session
  if (!s) return
  saving.value = true
  saveMessage.value = null
  try {
    await api.put(`/admin/setup/sessions/${s.id}/pricing`, body.value)
    toast.success(`Pricing saved for ${s.name}. Existing registrations keep their price.`)
    await load()
  } catch (e) {
    saveMessage.value = saveError(e).message
  } finally {
    saving.value = false
  }
}
const basisLabel: Record<RefundBasis, string> = {
  AmountPaid: 'of amount paid',
  BeyondDeposit: 'of amount beyond the deposit',
}
</script>

<template>
  <AdminOnly v-if="forbidden" page="Pricing and policies" />
  <Alert v-else-if="error" variant="destructive" class="mx-auto max-w-6xl"
    ><AlertDescription>{{ error }}</AlertDescription></Alert
  >
  <Skeleton v-else-if="!data" class="mx-auto h-96 max-w-6xl rounded-xl" />
  <div v-else class="mx-auto max-w-6xl space-y-6">
    <div class="flex flex-wrap items-start justify-between gap-4">
      <div>
        <h1 class="text-2xl font-semibold tracking-tight">Pricing and policies</h1>
        <p class="text-muted-foreground">
          Price, payment plan and cancellation policy for {{ data.session.program }} · {{ data.session.name }}. Change
          the session with the scope picker.
        </p>
      </div>
      <div class="flex items-center gap-3">
        <span v-if="dirty" class="flex items-center gap-1.5 text-sm text-amber-700"
          ><span class="size-2 rounded-full bg-amber-500" aria-hidden="true" />Unsaved changes</span
        >
        <Button v-if="dirty" variant="outline" :disabled="saving" @click="reset">Discard</Button>
        <Button :disabled="saving || !dirty || problems.length > 0" @click="save">{{
          saving ? 'Saving…' : 'Save pricing'
        }}</Button>
      </div>
    </div>
    <Alert v-if="data.registrations > 0">
      <TriangleAlert />
      <AlertDescription
        >{{ data.registrations }} campers are registered. They keep the price and plan they signed up with; changes
        apply to new registrations.</AlertDescription
      >
    </Alert>
    <p v-if="saveMessage" class="text-sm text-destructive" role="alert">{{ saveMessage }}</p>

    <div class="grid gap-6 lg:grid-cols-[1fr_24rem]">
      <div class="min-w-0 space-y-6">
        <Card>
          <CardHeader><CardTitle>Base pricing</CardTitle><CardDescription>Per camper.</CardDescription></CardHeader>
          <CardContent class="grid gap-4 sm:grid-cols-3">
            <div class="space-y-2">
              <Label for="price">Total price ($)</Label>
              <Input
                id="price"
                v-model="form.price"
                inputmode="decimal"
                :aria-invalid="!!err('priceCents') || undefined"
              />
              <p v-if="err('priceCents')" class="text-sm text-destructive">{{ err('priceCents') }}</p>
            </div>
            <div class="space-y-2">
              <Label for="deposit">Deposit ($)</Label>
              <Input
                id="deposit"
                v-model="form.deposit"
                inputmode="decimal"
                :aria-invalid="!!err('depositCents') || undefined"
              />
              <p v-if="err('depositCents')" class="text-sm text-destructive">{{ err('depositCents') }}</p>
            </div>
            <div class="space-y-2">
              <Label>Remaining balance</Label>
              <p class="flex h-9 items-center rounded-md border bg-muted/40 px-3 text-sm tabular-nums">
                {{ money(preview?.remainingCents) }}
              </p>
            </div>
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle>Payment plan</CardTitle>
            <CardDescription
              >The balance after the deposit, split into monthly payments ending on the due date. It has to be before
              {{ date(data.session.startDate) }}.</CardDescription
            >
          </CardHeader>
          <CardContent class="space-y-4">
            <div class="grid gap-4 sm:grid-cols-2">
              <div class="space-y-2">
                <Label for="installments">Installments</Label>
                <Select v-model="form.planInstallments">
                  <SelectTrigger id="installments" class="w-full"><SelectValue /></SelectTrigger>
                  <SelectContent>
                    <SelectItem value="0">No plan (balance due in one payment)</SelectItem>
                    <SelectItem v-for="n in 6" :key="n" :value="String(n)"
                      >{{ n }} monthly {{ n === 1 ? 'payment' : 'payments' }}</SelectItem
                    >
                  </SelectContent>
                </Select>
                <p v-if="err('planInstallments')" class="text-sm text-destructive">{{ err('planInstallments') }}</p>
              </div>
              <div class="space-y-2">
                <Label for="due">Balance due by</Label>
                <Input
                  id="due"
                  v-model="form.balanceDueDate"
                  type="date"
                  :aria-invalid="!!err('balanceDueDate') || undefined"
                />
                <p v-if="err('balanceDueDate')" class="text-sm text-destructive">{{ err('balanceDueDate') }}</p>
              </div>
            </div>
            <Table>
              <TableHeader
                ><TableRow
                  ><TableHead>Payment</TableHead><TableHead>Due</TableHead
                  ><TableHead class="text-right">Amount</TableHead></TableRow
                ></TableHeader
              >
              <TableBody>
                <TableRow v-for="p in preview?.schedule" :key="p.label">
                  <TableCell>{{ p.label }}</TableCell>
                  <TableCell>{{ date(p.dueDate) }}</TableCell>
                  <TableCell class="text-right tabular-nums">{{ money(p.amountCents) }}</TableCell>
                </TableRow>
              </TableBody>
            </Table>
            <p
              v-if="preview && preview.scheduleTotalCents === preview.totalCents"
              class="flex items-center gap-1.5 text-sm text-emerald-700"
            >
              <CircleCheck class="size-4" />Payments total {{ money(preview.scheduleTotalCents) }}, the full price.
            </p>
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle>Cancellation and refund policy</CardTitle>
            <CardDescription
              >Refunds staff offer when cancelling. The last window has to start 0 days before the
              session.</CardDescription
            >
          </CardHeader>
          <CardContent class="space-y-3">
            <div
              v-for="(t, i) in form.tiers"
              :key="i"
              class="grid grid-cols-2 items-end gap-3 rounded-lg border p-3 sm:grid-cols-[6rem_6rem_1fr_6rem_auto]"
            >
              <div class="space-y-1">
                <Label :for="`t-days-${i}`" class="text-xs">Days before</Label>
                <Input :id="`t-days-${i}`" v-model="t.daysBefore" type="number" min="0" />
              </div>
              <div class="space-y-1">
                <Label :for="`t-pct-${i}`" class="text-xs">Refund %</Label>
                <Input :id="`t-pct-${i}`" v-model="t.refundPercent" type="number" min="0" max="100" />
              </div>
              <div class="col-span-2 space-y-1 sm:col-span-1">
                <Label :for="`t-basis-${i}`" class="text-xs">Refund of</Label>
                <Select v-model="t.basis">
                  <SelectTrigger :id="`t-basis-${i}`" class="w-full"><SelectValue /></SelectTrigger>
                  <SelectContent>
                    <SelectItem value="AmountPaid">Amount paid</SelectItem>
                    <SelectItem value="BeyondDeposit">Amount paid beyond the deposit</SelectItem>
                  </SelectContent>
                </Select>
              </div>
              <div class="space-y-1">
                <Label :for="`t-fee-${i}`" class="text-xs">Admin fee ($)</Label>
                <Input :id="`t-fee-${i}`" v-model="t.adminFee" inputmode="decimal" />
              </div>
              <Button
                variant="ghost"
                size="icon"
                :aria-label="`Remove window ${i + 1}`"
                :disabled="form.tiers.length === 1"
                @click="form.tiers.splice(i, 1)"
                ><Trash2
              /></Button>
              <p class="col-span-full text-xs text-muted-foreground">
                {{ preview?.tiers.find((x) => x.daysBefore === Number(t.daysBefore))?.rule }}
              </p>
            </div>
            <p v-if="err('tiers')" class="text-sm text-destructive">{{ err('tiers') }}</p>
            <Button variant="outline" size="sm" :disabled="form.tiers.length >= 6" @click="addTier"
              ><Plus />Add window</Button
            >
          </CardContent>
        </Card>
      </div>

      <Card class="h-fit lg:sticky lg:top-4">
        <CardHeader
          ><CardTitle>What families see</CardTitle
          ><CardDescription>For one camper at checkout.</CardDescription></CardHeader
        >
        <CardContent v-if="preview">
          <Tabs default-value="pay">
            <TabsList class="w-full">
              <TabsTrigger value="pay">Tuition and payments</TabsTrigger>
              <TabsTrigger value="cancel">Cancellation</TabsTrigger>
            </TabsList>
            <TabsContent value="pay" class="space-y-3 pt-2">
              <p class="text-3xl font-semibold tabular-nums">{{ money(preview.totalCents) }}</p>
              <p class="text-sm text-muted-foreground">Total camp fee</p>
              <ul class="divide-y rounded-lg border text-sm">
                <li v-for="p in preview.schedule" :key="p.label" class="flex justify-between gap-3 p-3">
                  <span
                    ><span class="block">{{ p.label }}</span
                    ><span class="text-muted-foreground">Due {{ date(p.dueDate) }}</span></span
                  >
                  <span class="tabular-nums">{{ money(p.amountCents) }}</span>
                </li>
              </ul>
              <p class="text-xs text-muted-foreground">Everything is paid before {{ date(data.session.startDate) }}.</p>
            </TabsContent>
            <TabsContent value="cancel" class="pt-2">
              <ul class="space-y-3 text-sm">
                <li v-for="t in preview.tiers" :key="t.daysBefore" class="rounded-lg border p-3">
                  <p class="font-medium">
                    {{ t.from ? `${date(t.from)} – ${date(t.to)}` : `On or before ${date(t.to)}` }}
                  </p>
                  <p>
                    {{ t.refundPercent }}% refund {{ basisLabel[t.basis]
                    }}<template v-if="t.adminFeeCents">, less a {{ money(t.adminFeeCents) }} fee</template>
                  </p>
                  <p class="text-muted-foreground">Paid in full: {{ money(t.exampleRefundCents) }} back</p>
                </li>
              </ul>
            </TabsContent>
          </Tabs>
          <ul v-if="problems.length" class="mt-4 space-y-1 text-sm text-destructive" role="alert">
            <li v-for="p in problems" :key="p">{{ p }}</li>
          </ul>
        </CardContent>
      </Card>
    </div>
  </div>
</template>

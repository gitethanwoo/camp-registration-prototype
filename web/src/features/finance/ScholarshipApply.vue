<script setup lang="ts">
import { ArrowLeft, ArrowRight, CalendarDays, FileText, Info, MapPin, TriangleAlert, X } from '@lucide/vue'
import { computed, onMounted, ref } from 'vue'
import { RouterLink, useRouter } from 'vue-router'
import { toast } from 'vue-sonner'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Checkbox } from '@/components/ui/checkbox'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Skeleton } from '@/components/ui/skeleton'
import { Textarea } from '@/components/ui/textarea'
import { api, ApiError } from '@/lib/api'
import { dateRange, money } from '@/lib/format'
import type { FamilyScholarships } from './types'

// O6 · The financial assistance application for one registration (FR-090).
const props = defineProps<{ code: string }>()
const router = useRouter()

const data = ref<FamilyScholarships | null>(null)
const loadError = ref<string | null>(null)
const order = computed(() => data.value?.orders.find((o) => o.confirmationCode === props.code) ?? null)

const selected = ref<number[]>([])
const reason = ref('')
const requested = ref('')
const incomeBand = ref('')
const file = ref<File | null>(null)
const fileInput = ref<{ $el: HTMLInputElement } | null>(null)
const errors = ref<Record<string, string>>({})
const formError = ref<string | null>(null)
const busy = ref(false)
const REASON_MAX = 1000

onMounted(async () => {
  try {
    data.value = await api.get<FamilyScholarships>('/family/scholarships')
    selected.value = order.value?.campers.filter((c) => c.balanceCents > 0).map((c) => c.registrationId) ?? []
  } catch (e) {
    loadError.value = e instanceof ApiError ? e.message : "Couldn't load your registration."
  }
})

const campers = computed(() => order.value?.campers.filter((c) => selected.value.includes(c.registrationId)) ?? [])
const eligibleCents = computed(() => campers.value.reduce((s, c) => s + c.priceCents - c.discountCents, 0))
const requestedCents = computed(() => {
  const n = Number(requested.value)
  return Number.isFinite(n) && n > 0 ? Math.round(n * 100) : 0
})

function toggle(id: number, on: boolean) {
  selected.value = on ? [...new Set([...selected.value, id])] : selected.value.filter((x) => x !== id)
}
function pick(e: Event) {
  const input = e.target as HTMLInputElement
  const f = input.files?.[0] ?? null
  input.value = ''
  delete errors.value.document
  if (f && data.value && f.size > data.value.maxDocumentBytes) {
    errors.value.document = 'Files can be up to 5 MB.'
    return
  }
  file.value = f
}
const readBase64 = (f: File) =>
  new Promise<string>((resolve, reject) => {
    const reader = new FileReader()
    reader.addEventListener('load', () => resolve(String(reader.result).split(',')[1] ?? ''))
    reader.addEventListener('error', () => reject(reader.error))
    reader.readAsDataURL(f)
  })

function validate() {
  const e: Record<string, string> = {}
  if (!selected.value.length) e.registrationIds = 'Choose at least one camper.'
  if (!reason.value.trim()) e.reason = 'Tell us a little about your situation.'
  if (!requestedCents.value) e.requestedCents = "Enter the amount you're asking for."
  else if (requestedCents.value > eligibleCents.value)
    e.requestedCents = `You can ask for up to ${money(eligibleCents.value)}, the cost for these campers after discounts.`
  if (!incomeBand.value) e.incomeBand = 'Choose your household income range.'
  if (!file.value) e.document = 'A supporting document is required. Upload a tax return, pay stub or similar.'
  errors.value = e
  return Object.keys(e).length === 0
}

async function submit() {
  formError.value = null
  if (!validate() || !file.value) return
  busy.value = true
  try {
    await api.post('/family/scholarships', {
      code: props.code,
      registrationIds: selected.value,
      requestedCents: requestedCents.value,
      incomeBand: incomeBand.value,
      reason: reason.value.trim(),
      document: { fileName: file.value.name, contentType: file.value.type, base64: await readBase64(file.value) },
    })
    toast.success('Application submitted.', { description: "We'll email you when a decision is made." })
    await router.push('/family/scholarships')
  } catch (e) {
    if (e instanceof ApiError && Object.keys(e.errors).length)
      errors.value = Object.fromEntries(Object.entries(e.errors).map(([k, v]) => [k, v[0] ?? '']))
    else formError.value = e instanceof ApiError ? e.message : "That didn't go through. Try again."
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <div class="mx-auto max-w-6xl px-4 py-8 md:py-12">
    <Button variant="link" as-child class="h-auto p-0 text-muted-foreground">
      <RouterLink to="/family/scholarships"><ArrowLeft />Scholarships</RouterLink>
    </Button>

    <Alert v-if="loadError" variant="destructive" class="mt-6"
      ><AlertDescription>{{ loadError }}</AlertDescription></Alert
    >
    <Skeleton v-else-if="!data" class="mt-6 h-96 rounded-xl" />
    <div v-else-if="!order || !order.canApply" class="mt-6 space-y-3">
      <h1 class="text-3xl font-semibold tracking-tight">Apply for financial assistance</h1>
      <p class="text-muted-foreground">
        {{
          order
            ? 'You already have an application in review for this registration, or nothing is owed on it.'
            : "We couldn't find that registration on your account."
        }}
      </p>
    </div>

    <template v-else>
      <h1 class="mt-4 text-3xl font-semibold tracking-tight md:text-4xl">Apply for financial assistance</h1>
      <p class="mt-1 text-lg text-muted-foreground">
        {{ order.program }} · {{ order.location }} · {{ dateRange(order.startDate, order.endDate) }}
      </p>

      <div class="mt-6 grid gap-6 lg:grid-cols-[1fr_20rem]">
        <form class="space-y-6" novalidate @submit.prevent="submit">
          <Card>
            <CardHeader><CardTitle>Household information</CardTitle></CardHeader>
            <CardContent class="grid gap-4 sm:grid-cols-2">
              <div class="space-y-1">
                <p class="text-sm font-medium">Primary contact</p>
                <div class="rounded-md border bg-muted/40 px-3 py-2 text-sm">
                  <p>{{ data.household.contact }}</p>
                  <p class="break-all text-muted-foreground">{{ data.household.email }} · {{ data.household.phone }}</p>
                </div>
              </div>
              <div class="space-y-1">
                <p class="text-sm font-medium">Household</p>
                <div class="rounded-md border bg-muted/40 px-3 py-2 text-sm">
                  <p>{{ data.household.name }} family</p>
                  <p class="text-muted-foreground">{{ data.household.city }}</p>
                </div>
              </div>
            </CardContent>
          </Card>

          <Card>
            <CardHeader>
              <CardTitle>Campers attending this camp</CardTitle>
              <CardDescription>Select the campers you're requesting assistance for.</CardDescription>
            </CardHeader>
            <CardContent class="divide-y">
              <div
                v-for="c in order.campers"
                :key="c.registrationId"
                class="flex items-center gap-3 py-3 first:pt-0 last:pb-0"
              >
                <Checkbox
                  :id="`camper-${c.registrationId}`"
                  :model-value="selected.includes(c.registrationId)"
                  :disabled="c.balanceCents <= 0"
                  @update:model-value="(v) => toggle(c.registrationId, v === true)"
                />
                <Label :for="`camper-${c.registrationId}`" class="flex flex-1 flex-col items-start gap-0 font-normal">
                  <span class="font-medium">{{ c.name }}</span>
                  <span class="text-muted-foreground"
                    >Grade {{ c.grade }}{{ c.balanceCents <= 0 ? ' · paid in full' : '' }}</span
                  >
                </Label>
                <span class="tabular-nums">{{ money(c.priceCents - c.discountCents) }}</span>
              </div>
              <p v-if="errors.registrationIds" class="pt-2 text-sm text-destructive">{{ errors.registrationIds }}</p>
            </CardContent>
          </Card>

          <Card>
            <CardHeader><CardTitle>Assistance request details</CardTitle></CardHeader>
            <CardContent class="space-y-4">
              <div class="grid gap-4 sm:grid-cols-2">
                <div class="space-y-1">
                  <Label for="reason">Reason for requesting assistance</Label>
                  <Textarea
                    id="reason"
                    v-model="reason"
                    rows="5"
                    :maxlength="REASON_MAX"
                    :aria-invalid="!!errors.reason"
                    placeholder="Tell us about your situation and why you're requesting financial assistance."
                  />
                  <div class="flex justify-between text-xs">
                    <span class="text-destructive">{{ errors.reason }}</span>
                    <span class="text-muted-foreground">{{ reason.length }}/{{ REASON_MAX }}</span>
                  </div>
                </div>
                <div class="space-y-4">
                  <div class="space-y-1">
                    <Label for="requested">Requested assistance amount ($)</Label>
                    <Input
                      id="requested"
                      v-model="requested"
                      type="number"
                      inputmode="decimal"
                      min="0"
                      step="1"
                      :aria-invalid="!!errors.requestedCents"
                    />
                    <p class="text-xs" :class="errors.requestedCents ? 'text-destructive' : 'text-muted-foreground'">
                      {{ errors.requestedCents ?? `Up to ${money(eligibleCents)} for the campers selected.` }}
                    </p>
                  </div>
                  <div class="space-y-1">
                    <Label for="income">Household income band</Label>
                    <Select v-model="incomeBand">
                      <SelectTrigger id="income" class="w-full" :aria-invalid="!!errors.incomeBand"
                        ><SelectValue placeholder="Select an income range"
                      /></SelectTrigger>
                      <SelectContent>
                        <SelectItem v-for="b in data.incomeBands" :key="b" :value="b">{{ b }}</SelectItem>
                      </SelectContent>
                    </Select>
                    <p v-if="errors.incomeBand" class="text-xs text-destructive">{{ errors.incomeBand }}</p>
                  </div>
                </div>
              </div>

              <div class="space-y-1">
                <p class="text-sm font-medium">Supporting document</p>
                <p class="text-xs text-muted-foreground">
                  A tax return, pay stub or other financial document. PDF, PNG or JPEG up to 5 MB.
                </p>
                <div class="flex items-center gap-3 rounded-md border px-3 py-2">
                  <FileText class="size-4 shrink-0 text-muted-foreground" />
                  <span class="min-w-0 flex-1 truncate text-sm" data-testid="file-name">{{
                    file ? file.name : 'No file selected'
                  }}</span>
                  <Button v-if="file" type="button" variant="ghost" size="sm" @click="file = null"><X />Remove</Button>
                  <Button type="button" variant="outline" size="sm" @click="fileInput?.$el.click()"
                    >Choose a file</Button
                  >
                  <Input
                    ref="fileInput"
                    type="file"
                    accept="application/pdf,image/png,image/jpeg"
                    class="hidden"
                    aria-label="Supporting document"
                    @change="pick"
                  />
                </div>
                <Alert v-if="errors.document" variant="destructive">
                  <TriangleAlert />
                  <AlertTitle>{{ errors.document }}</AlertTitle>
                </Alert>
              </div>

              <p class="flex items-center gap-2 text-sm text-muted-foreground">
                <Info class="size-4" />We will review this request before any award is applied.
              </p>
              <p v-if="formError" class="text-sm text-destructive" role="alert">{{ formError }}</p>
              <Button type="submit" size="lg" class="w-full sm:w-auto" :disabled="busy">
                {{ busy ? 'Submitting…' : 'Submit application' }}<ArrowRight />
              </Button>
            </CardContent>
          </Card>
        </form>

        <Card class="h-fit lg:sticky lg:top-6">
          <CardHeader><CardTitle>Application summary</CardTitle></CardHeader>
          <CardContent class="space-y-4 text-sm">
            <div class="space-y-2">
              <p class="flex gap-2">
                <CalendarDays class="size-4 shrink-0 text-muted-foreground" /><span
                  ><span class="font-medium">{{ order.program }}</span
                  ><br />{{ dateRange(order.startDate, order.endDate) }}</span
                >
              </p>
              <p class="flex gap-2"><MapPin class="size-4 shrink-0 text-muted-foreground" />{{ order.location }}</p>
            </div>
            <div class="space-y-1 border-t pt-3">
              <p class="flex justify-between font-medium">
                <span>Selected campers</span><span>{{ campers.length }}</span>
              </p>
              <p v-for="c in campers" :key="c.registrationId" class="flex justify-between text-muted-foreground">
                <span>{{ c.name }} (Grade {{ c.grade }})</span
                ><span class="tabular-nums">{{ money(c.priceCents - c.discountCents) }}</span>
              </p>
            </div>
            <p class="flex items-baseline justify-between border-t pt-3">
              <span class="font-medium">Total camp cost</span
              ><span class="text-2xl font-semibold tabular-nums">{{ money(eligibleCents) }}</span>
            </p>
            <p class="flex justify-between text-muted-foreground">
              <span>Balance due now</span
              ><span class="tabular-nums">{{ money(campers.reduce((s, c) => s + c.balanceCents, 0)) }}</span>
            </p>
          </CardContent>
        </Card>
      </div>
    </template>
  </div>
</template>

<script setup lang="ts">
import { Info, TriangleAlert } from '@lucide/vue'
import { computed, reactive, ref, watch } from 'vue'
import { RouterLink } from 'vue-router'
import { toast } from 'vue-sonner'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Sheet, SheetContent, SheetDescription, SheetFooter, SheetHeader, SheetTitle } from '@/components/ui/sheet'
import { api } from '@/lib/api'
import { money } from '@/lib/format'
import PublishBadge from './PublishBadge.vue'
import type { DiscountKind, DiscountPreview, DiscountRuleRow, DiscountRules } from './types'
import { saveError, toCents, toDollars } from './useSetupLoad'

const props = defineProps<{ open: boolean; rule: DiscountRuleRow | null; scopes: DiscountRules['scopes'] }>()
const emit = defineEmits<{ close: []; saved: [id?: number] }>()

const today = new Date().toISOString().slice(0, 10)
const form = reactive({
  code: '',
  name: '',
  kind: 'Percent' as DiscountKind,
  amount: '',
  programId: 'all',
  sessionId: 'all',
  validFrom: today,
  validTo: '',
  maxUses: '',
  stackable: 'no',
})
const errors = ref<Record<string, string[]>>({})
const message = ref<string | null>(null)
const busy = ref(false)
const readOnly = computed(() => !!props.rule && !props.rule.editable)
// Preview on a real registered camper, plus the combination guard, from the server.
const preview = ref<DiscountPreview | null>(null)
const camper = ref<string>('')
let timer: ReturnType<typeof setTimeout> | undefined

watch(
  () => [props.open, props.rule?.id] as const,
  () => {
    const r = props.rule
    Object.assign(form, {
      code: r?.code ?? '',
      name: r?.hasRule ? r.name : '',
      kind: r?.kind ?? 'Percent',
      amount: amountText(r),
      programId: r?.programId ? String(r.programId) : 'all',
      sessionId: r?.sessionId ? String(r.sessionId) : 'all',
      validFrom: r?.validFrom ?? today,
      validTo: r?.validTo ?? '',
      maxUses: r?.maxUses != null ? String(r.maxUses) : '',
      stackable: r?.stackable ? 'yes' : 'no',
    })
    errors.value = {}
    message.value = null
    preview.value = null
  },
  { immediate: true },
)
const program = computed(() => props.scopes.find((p) => String(p.id) === form.programId))
watch(
  () => form.programId,
  () => {
    if (!program.value?.sessions.some((s) => String(s.id) === form.sessionId)) form.sessionId = 'all'
  },
)

const body = computed(() => ({
  code: form.code.trim().toUpperCase(),
  name: form.name,
  kind: form.kind,
  value: form.kind === 'Percent' ? Number(form.amount) || 0 : toCents(form.amount) || 0,
  programId: form.programId === 'all' ? null : Number(form.programId),
  sessionId: form.sessionId === 'all' ? null : Number(form.sessionId),
  validFrom: form.validFrom,
  validTo: form.validTo,
  maxUses: form.maxUses === '' ? null : Number(form.maxUses),
  stackable: form.stackable === 'yes',
}))

// Runs once both dates are set.
watch(
  [body, camper, () => props.open],
  () => {
    if (!props.open || readOnly.value || !form.validFrom || !form.validTo) return
    clearTimeout(timer)
    timer = setTimeout(async () => {
      try {
        preview.value = await api.post<DiscountPreview>('/admin/setup/discount-rules/preview', {
          ...body.value,
          codeId: props.rule?.id ?? null,
          registrationId: camper.value ? Number(camper.value) : null,
        })
      } catch {
        preview.value = null
      }
    }, 250)
  },
  { deep: true, immediate: true },
)

async function save() {
  busy.value = true
  errors.value = {}
  message.value = null
  try {
    if (props.rule) {
      await api.put(`/admin/setup/discount-rules/${props.rule.id}`, body.value)
      toast.success(`${body.value.code} saved. Orders already placed keep their discount.`)
      emit('saved', props.rule.id)
    } else {
      await api.post('/admin/setup/discount-rules', body.value)
      toast.success(`${body.value.code} is live at checkout from its start date.`)
      emit('saved')
    }
  } catch (e) {
    const err = saveError(e)
    message.value = Object.keys(err.fields).length ? null : err.message
    errors.value = err.fields
  } finally {
    busy.value = false
  }
}
async function toggle() {
  const r = props.rule
  if (!r) return
  busy.value = true
  try {
    await api.post(`/admin/setup/discount-rules/${r.id}/${r.active ? 'deactivate' : 'activate'}`)
    toast.success(r.active ? `${r.code} is off. Families see it as an invalid code.` : `${r.code} is back on.`)
    emit('saved', r.id)
  } catch (e) {
    message.value = saveError(e).message
  } finally {
    busy.value = false
  }
}
const err = (k: string) => errors.value[k]?.[0]
function amountText(r: DiscountRuleRow | null) {
  if (!r) return ''
  return r.kind === 'Percent' ? String(r.value) : toDollars(r.value)
}
</script>

<template>
  <Sheet :open="open" @update:open="(v) => !v && emit('close')">
    <SheetContent class="w-full overflow-y-auto sm:max-w-lg">
      <SheetHeader>
        <SheetTitle class="text-xl">{{ rule ? `${rule.code}` : 'New discount rule' }}</SheetTitle>
        <SheetDescription class="flex flex-wrap items-center gap-2">
          <template v-if="rule"><PublishBadge :state="rule.status" /> {{ rule.statusReason }}</template>
          <template v-else>Live at checkout from the start date once you save.</template>
        </SheetDescription>
      </SheetHeader>

      <div class="space-y-4 px-4">
        <Alert v-if="readOnly">
          <Info />
          <AlertTitle class="line-clamp-none">{{ rule!.source }}</AlertTitle>
          <AlertDescription>
            Host and partner codes are approved or rejected in Discount approvals, with the terms the requester asked
            for.
            <RouterLink to="/admin/discounts" class="underline underline-offset-4">Open Discount approvals</RouterLink>
          </AlertDescription>
        </Alert>

        <form id="rule-form" class="grid gap-4 sm:grid-cols-2" @submit.prevent="save">
          <div class="space-y-2">
            <Label for="r-code">Code</Label>
            <Input
              id="r-code"
              v-model="form.code"
              class="font-mono uppercase"
              :disabled="!!rule"
              placeholder="SIBLING10"
              :aria-invalid="!!err('code') || undefined"
            />
            <p v-if="err('code')" class="text-sm text-destructive">{{ err('code') }}</p>
          </div>
          <div class="space-y-2">
            <Label for="r-name">Name</Label>
            <Input
              id="r-name"
              v-model="form.name"
              :disabled="readOnly"
              placeholder="Sibling discount"
              :aria-invalid="!!err('name') || undefined"
            />
            <p v-if="err('name')" class="text-sm text-destructive">{{ err('name') }}</p>
          </div>
          <div class="space-y-2">
            <Label for="r-kind">Discount type</Label>
            <Select v-model="form.kind" :disabled="readOnly">
              <SelectTrigger id="r-kind" class="w-full"><SelectValue /></SelectTrigger>
              <SelectContent>
                <SelectItem value="Percent">Percentage</SelectItem>
                <SelectItem value="Flat">Flat amount per camper</SelectItem>
              </SelectContent>
            </Select>
          </div>
          <div class="space-y-2">
            <Label for="r-amount">{{ form.kind === 'Percent' ? 'Percent off' : 'Amount off ($)' }}</Label>
            <Input
              id="r-amount"
              v-model="form.amount"
              inputmode="decimal"
              :disabled="readOnly"
              :aria-invalid="!!err('value') || undefined"
            />
          </div>
          <div class="space-y-2">
            <Label for="r-program">Applies to</Label>
            <Select v-model="form.programId" :disabled="readOnly">
              <SelectTrigger id="r-program" class="w-full"><SelectValue /></SelectTrigger>
              <SelectContent>
                <SelectItem value="all">All programs</SelectItem>
                <SelectItem v-for="p in scopes" :key="p.id" :value="String(p.id)">{{ p.name }}</SelectItem>
              </SelectContent>
            </Select>
          </div>
          <div class="space-y-2">
            <Label for="r-session">Session</Label>
            <Select v-model="form.sessionId" :disabled="readOnly || !program">
              <SelectTrigger id="r-session" class="w-full"><SelectValue /></SelectTrigger>
              <SelectContent>
                <SelectItem value="all">All sessions</SelectItem>
                <SelectItem v-for="s in program?.sessions ?? []" :key="s.id" :value="String(s.id)"
                  >{{ s.name }} · {{ money(s.priceCents) }}</SelectItem
                >
              </SelectContent>
            </Select>
          </div>
          <div class="space-y-2">
            <Label for="r-from">Valid from</Label>
            <Input id="r-from" v-model="form.validFrom" type="date" :disabled="readOnly" />
          </div>
          <div class="space-y-2">
            <Label for="r-to">Valid through</Label>
            <Input
              id="r-to"
              v-model="form.validTo"
              type="date"
              :disabled="readOnly"
              :aria-invalid="!!err('validTo') || undefined"
            />
            <p v-if="err('validTo')" class="text-sm text-destructive">{{ err('validTo') }}</p>
          </div>
          <div class="space-y-2">
            <Label for="r-cap">Usage cap <span class="font-normal text-muted-foreground">(optional)</span></Label>
            <Input
              id="r-cap"
              v-model="form.maxUses"
              type="number"
              min="1"
              :disabled="readOnly"
              placeholder="No cap"
              :aria-invalid="!!err('maxUses') || undefined"
            />
            <p v-if="err('maxUses')" class="text-sm text-destructive">{{ err('maxUses') }}</p>
            <p v-else-if="rule" class="text-xs text-muted-foreground">Used {{ rule.uses }} times so far.</p>
          </div>
          <div class="space-y-2">
            <Label for="r-stack">Stacking</Label>
            <Select v-model="form.stackable" :disabled="readOnly">
              <SelectTrigger id="r-stack" class="w-full"><SelectValue /></SelectTrigger>
              <SelectContent>
                <SelectItem value="no">Doesn't stack</SelectItem>
                <SelectItem value="yes">Stacks with other codes</SelectItem>
              </SelectContent>
            </Select>
          </div>
        </form>

        <Alert v-if="err('value') || preview?.guard" variant="destructive" role="alert">
          <TriangleAlert />
          <AlertTitle class="line-clamp-none">{{
            preview?.guard ? "Combined discounts can't go over 100%" : 'Check the discount amount'
          }}</AlertTitle>
          <AlertDescription>{{ err('value') ?? preview?.guard }}</AlertDescription>
        </Alert>

        <div v-if="!readOnly" class="rounded-lg border bg-muted/30 p-4">
          <p class="text-sm font-medium">Discount preview</p>
          <template v-if="preview?.camper">
            <p class="mt-1 text-lg font-semibold tabular-nums" data-testid="discount-preview">
              {{ preview.camper.firstName }}: {{ money(preview.camper.priceCents) }} →
              {{ money(preview.camper.finalCents) }}
            </p>
            <p class="text-sm text-emerald-700">
              {{ money(preview.camper.discountCents) }} off ({{ preview.camper.percent }}%) ·
              {{ preview.camper.session }}
            </p>
            <Select v-if="preview.campers.length > 1" v-model="camper">
              <SelectTrigger class="mt-3 w-full" aria-label="Preview camper"
                ><SelectValue placeholder="Try another camper"
              /></SelectTrigger>
              <SelectContent>
                <SelectItem v-for="c in preview.campers" :key="c.registrationId" :value="String(c.registrationId)"
                  >{{ c.name }} · {{ c.session }}</SelectItem
                >
              </SelectContent>
            </Select>
          </template>
          <p v-else-if="!form.validFrom || !form.validTo" class="mt-1 text-sm text-muted-foreground">
            Pick the valid dates to preview this rule on a registered camper.
          </p>
          <p v-else class="mt-1 text-sm text-muted-foreground">
            No registered campers in this scope to preview on yet.
          </p>
        </div>
        <p v-if="message" class="text-sm text-destructive" role="alert">{{ message }}</p>
      </div>

      <SheetFooter v-if="!readOnly" class="flex-row flex-wrap gap-2">
        <Button type="submit" form="rule-form" :disabled="busy || preview?.blocked">{{
          rule ? 'Save rule' : 'Create rule'
        }}</Button>
        <Button v-if="rule?.hasRule" variant="outline" :disabled="busy" @click="toggle">{{
          rule.active ? 'Turn off' : 'Turn on'
        }}</Button>
      </SheetFooter>
    </SheetContent>
  </Sheet>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { Checkbox } from '@/components/ui/checkbox'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { RadioGroup, RadioGroupItem } from '@/components/ui/radio-group'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Textarea } from '@/components/ui/textarea'
import type { FormQuestion } from './types'

// One registration question as a family sees it. The wizard (R3) and the builder's phone preview
// (K6) both render through this, so the preview is what families get.

const props = defineProps<{
  question: FormQuestion
  /** Unique on the page: the wizard passes one per camper, so labels and inputs pair correctly. */
  fieldId: string
  invalid?: boolean
  /** Lay the field out in one column (the phone preview). */
  compact?: boolean
}>()
const model = defineModel<string>({ default: '' })

const q = computed(() => props.question)
const wide = computed(() => !props.compact && ['LongText', 'MultipleChoice'].includes(q.value.type))
const labelId = computed(() => `${props.fieldId}-label`)
// Radio and checkbox groups are named by aria-labelledby; each option is named by its own text.
const grouped = computed(() => q.value.type === 'YesNo' || q.value.type === 'MultipleChoice')
const helpId = computed(() => (q.value.helpText ? `${props.fieldId}-help` : undefined))

const picked = computed(() => (model.value ? model.value.split('|') : []))
function pick(option: string, on: boolean | 'indeterminate') {
  const next = new Set(picked.value)
  if (on === true) next.add(option)
  else next.delete(option)
  // Stored in the form's order, the way the server keeps it.
  model.value = q.value.options.filter((o) => next.has(o)).join('|')
}
</script>

<template>
  <div :class="['space-y-2', wide && 'sm:col-span-2']">
    <Label :id="labelId" :for="grouped ? undefined : fieldId" class="leading-snug"
      >{{ q.label }}<span v-if="q.required" class="text-destructive" aria-hidden="true"> *</span></Label
    >
    <p v-if="q.helpText" :id="helpId" class="text-sm text-muted-foreground">{{ q.helpText }}</p>

    <Select v-if="q.type === 'Select' || q.type === 'SingleChoice'" v-model="model">
      <SelectTrigger
        :id="fieldId"
        class="w-full"
        :aria-describedby="helpId"
        :aria-required="q.required || undefined"
        :aria-invalid="invalid || undefined"
      >
        <SelectValue placeholder="Choose…" />
      </SelectTrigger>
      <SelectContent>
        <SelectItem v-for="o in q.options" :key="o" :value="o">{{ o }}</SelectItem>
      </SelectContent>
    </Select>

    <RadioGroup
      v-else-if="q.type === 'YesNo'"
      v-model="model"
      class="flex gap-6"
      :aria-labelledby="labelId"
      :aria-describedby="helpId"
      :aria-required="q.required || undefined"
      :aria-invalid="invalid || undefined"
    >
      <Label v-for="o in ['Yes', 'No']" :key="o" class="font-normal"
        ><RadioGroupItem :value="o" :aria-label="o" />{{ o }}</Label
      >
    </RadioGroup>

    <div
      v-else-if="q.type === 'MultipleChoice'"
      role="group"
      :aria-labelledby="labelId"
      :aria-describedby="helpId"
      :class="['grid gap-2', !compact && 'sm:grid-cols-2']"
    >
      <Label v-for="o in q.options" :key="o" class="font-normal"
        ><Checkbox :model-value="picked.includes(o)" @update:model-value="pick(o, $event)" />{{ o }}</Label
      >
    </div>

    <Textarea
      v-else-if="q.type === 'LongText'"
      :id="fieldId"
      v-model="model"
      rows="3"
      maxlength="2000"
      :aria-describedby="helpId"
      :aria-required="q.required || undefined"
      :aria-invalid="invalid || undefined"
    />

    <Input
      v-else
      :id="fieldId"
      v-model="model"
      :type="q.type === 'Date' ? 'date' : 'text'"
      :inputmode="q.type === 'Number' ? 'decimal' : undefined"
      :maxlength="q.type === 'Date' ? undefined : 200"
      :class="q.type === 'Number' ? 'max-w-40' : q.type === 'Date' ? 'max-w-48' : undefined"
      :aria-describedby="helpId"
      :aria-required="q.required || undefined"
      :aria-invalid="invalid || undefined"
    />
  </div>
</template>

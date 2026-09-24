<script setup lang="ts">
import { ArrowDown, ArrowUp, Plus, Trash2, X } from '@lucide/vue'
import { computed } from 'vue'
import { Button } from '@/components/ui/button'
import { Checkbox } from '@/components/ui/checkbox'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { RadioGroup, RadioGroupItem } from '@/components/ui/radio-group'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { conditionValues, isChoice, keyFromLabel, typeLabels } from './rules'
import type { FormQuestion, FormQuestionType, QuestionScope } from './types'

// K6 · the properties of one question in a draft. Every change replaces the question object, so the
// builder's autosave sees it.

const props = defineProps<{
  /** The questions above this one: conditions can only point back up the form. */
  earlier: FormQuestion[]
  /** Keys used by the other questions in the draft. */
  otherKeys: Set<string>
  /** Keys in the live version: renaming one would split answers across versions. */
  liveKeys: Set<string>
  index: number
  count: number
  readonly: boolean
  errors: Record<string, string[]>
}>()
const q = defineModel<FormQuestion>({ required: true })
const emit = defineEmits<{ rename: [from: string, to: string]; move: [by: number]; remove: [] }>()

const err = (field: string) => props.errors[`questions.${props.index}.${field}`]?.[0]
function set(patch: Partial<FormQuestion>) {
  q.value = { ...q.value, ...patch }
}

function setLabel(label: string) {
  // A new question's key follows its label until someone edits the key.
  const q0 = q.value
  const derived = !props.liveKeys.has(q0.key) && q0.key === keyFromLabel(q0.label, props.otherKeys)
  if (!derived) {
    set({ label })
    return
  }
  const key = keyFromLabel(label || 'question', props.otherKeys)
  set({ label, key })
  if (key !== q0.key) emit('rename', q0.key, key)
}
function setKey(key: string) {
  const from = q.value.key
  set({ key })
  emit('rename', from, key)
}
function setType(type: FormQuestionType) {
  if (isChoice(type)) set({ type, options: q.value.options.length >= 2 ? q.value.options : ['Option 1', 'Option 2'] })
  else set({ type, options: [] })
}
const setOption = (i: number, value: string) => set({ options: q.value.options.map((o, j) => (j === i ? value : o)) })
const removeOption = (i: number) => set({ options: q.value.options.filter((_, j) => j !== i) })
const addOption = () => set({ options: [...q.value.options, `Option ${q.value.options.length + 1}`] })

// A household question is asked before any camper's, so it can only depend on another household question.
const conditionSources = computed(() =>
  props.earlier.filter(
    (e) => conditionValues(e).length && !(q.value.scope === 'Household' && e.scope === 'Participant'),
  ),
)
const source = computed(() => props.earlier.find((e) => e.key === q.value.showWhenKey) ?? null)
const always = '__always'
function setCondition(key: string) {
  if (key === always) {
    set({ showWhenKey: null, showWhenValue: null })
    return
  }
  const from = props.earlier.find((e) => e.key === key)
  set({ showWhenKey: key, showWhenValue: from ? (conditionValues(from)[0] ?? null) : null })
}
const types = Object.entries(typeLabels) as [FormQuestionType, string][]
const id = (field: string) => `qe-${field}`
</script>

<template>
  <fieldset :disabled="readonly" class="space-y-5">
    <legend class="sr-only">Question {{ index + 1 }}</legend>
    <div class="space-y-2">
      <Label :for="id('label')">Question</Label>
      <Input
        :id="id('label')"
        :model-value="q.label"
        maxlength="200"
        :aria-invalid="!!err('label') || undefined"
        @update:model-value="setLabel(String($event))"
      />
      <p v-if="err('label')" class="text-sm text-destructive">{{ err('label') }}</p>
    </div>

    <div class="space-y-2">
      <Label :for="id('help')">Help text <span class="font-normal text-muted-foreground">(optional)</span></Label>
      <Input
        :id="id('help')"
        :model-value="q.helpText ?? ''"
        maxlength="300"
        @update:model-value="set({ helpText: String($event) || null })"
      />
    </div>

    <div class="space-y-2">
      <Label :for="id('type')">Answer type</Label>
      <Select :model-value="q.type" :disabled="readonly" @update:model-value="setType($event as FormQuestionType)">
        <SelectTrigger :id="id('type')" class="w-full"><SelectValue /></SelectTrigger>
        <SelectContent>
          <SelectItem v-for="[value, label] in types" :key="value" :value="value">{{ label }}</SelectItem>
        </SelectContent>
      </Select>
    </div>

    <div v-if="isChoice(q.type)" class="space-y-2">
      <p id="qe-choices" class="text-sm font-medium">Choices</p>
      <ul class="space-y-2" aria-labelledby="qe-choices">
        <li v-for="(o, i) in q.options" :key="i" class="flex items-center gap-2">
          <Input
            :model-value="o"
            :aria-label="`Choice ${i + 1}`"
            maxlength="100"
            @update:model-value="setOption(i, String($event))"
          />
          <Button
            v-if="!readonly"
            variant="ghost"
            size="icon"
            :aria-label="`Remove choice ${i + 1}`"
            :disabled="q.options.length <= 2"
            @click="removeOption(i)"
            ><X
          /></Button>
        </li>
      </ul>
      <p v-if="err('options')" class="text-sm text-destructive">{{ err('options') }}</p>
      <Button v-if="!readonly" variant="outline" size="sm" @click="addOption"><Plus />Add choice</Button>
    </div>

    <div class="space-y-2">
      <p id="qe-scope" class="text-sm font-medium">Asked</p>
      <RadioGroup
        :model-value="q.scope"
        aria-labelledby="qe-scope"
        :disabled="readonly"
        class="gap-3"
        @update:model-value="set({ scope: $event as QuestionScope })"
      >
        <Label class="items-start font-normal"
          ><RadioGroupItem value="Participant" class="mt-0.5" /><span
            ><span class="block">Once per camper</span
            ><span class="block text-muted-foreground">Families answer for each child they register.</span></span
          ></Label
        >
        <Label class="items-start font-normal"
          ><RadioGroupItem value="Household" class="mt-0.5" /><span
            ><span class="block">Once per family</span
            ><span class="block text-muted-foreground">One answer for everyone on the registration.</span></span
          ></Label
        >
      </RadioGroup>
    </div>

    <Label class="font-normal"
      ><Checkbox
        :model-value="q.required"
        :disabled="readonly"
        @update:model-value="set({ required: $event === true })"
      />Required</Label
    >

    <div class="space-y-2">
      <Label :for="id('when')">Show this question</Label>
      <Select
        :model-value="q.showWhenKey ?? always"
        :disabled="readonly"
        @update:model-value="setCondition(String($event))"
      >
        <SelectTrigger :id="id('when')" class="w-full" :aria-invalid="!!err('showWhenKey') || undefined"
          ><SelectValue
        /></SelectTrigger>
        <SelectContent>
          <SelectItem :value="always">Always</SelectItem>
          <SelectItem v-for="e in conditionSources" :key="e.key" :value="e.key">When “{{ e.label }}” is…</SelectItem>
          <SelectItem v-if="q.showWhenKey && !source" :value="q.showWhenKey">{{ q.showWhenKey }}</SelectItem>
        </SelectContent>
      </Select>
      <p v-if="err('showWhenKey')" class="text-sm text-destructive">{{ err('showWhenKey') }}</p>
      <template v-if="source">
        <Label :for="id('value')" class="sr-only">Answer that shows this question</Label>
        <Select
          :model-value="q.showWhenValue ?? ''"
          :disabled="readonly"
          @update:model-value="set({ showWhenValue: String($event) })"
        >
          <SelectTrigger :id="id('value')" class="w-full"><SelectValue placeholder="Choose an answer" /></SelectTrigger>
          <SelectContent>
            <SelectItem v-for="v in conditionValues(source)" :key="v" :value="v">{{ v }}</SelectItem>
          </SelectContent>
        </Select>
        <p v-if="err('showWhenValue')" class="text-sm text-destructive">{{ err('showWhenValue') }}</p>
        <p class="text-sm text-muted-foreground">
          Hidden answers aren't saved, and a required question is only required while it shows.
        </p>
      </template>
    </div>

    <div class="space-y-2">
      <Label :for="id('key')">Report key</Label>
      <Input
        :id="id('key')"
        :model-value="q.key"
        maxlength="40"
        class="font-mono"
        :aria-invalid="!!err('key') || undefined"
        aria-describedby="qe-key-help"
        @update:model-value="setKey(String($event))"
      />
      <p v-if="err('key')" class="text-sm text-destructive">{{ err('key') }}</p>
      <p id="qe-key-help" class="text-sm text-muted-foreground">
        Used in exports. Letters and numbers, starting with a lowercase letter.
        <template v-if="liveKeys.has(q.key)">Keep it the same so answers line up across versions.</template>
      </p>
    </div>

    <div v-if="!readonly" class="flex flex-wrap gap-2 border-t pt-4">
      <Button variant="outline" size="sm" :disabled="index === 0" @click="emit('move', -1)"><ArrowUp />Move up</Button>
      <Button variant="outline" size="sm" :disabled="index === count - 1" @click="emit('move', 1)"
        ><ArrowDown />Move down</Button
      >
      <Button variant="ghost" size="sm" class="text-destructive sm:ml-auto" @click="emit('remove')"
        ><Trash2 />Remove question</Button
      >
    </div>
  </fieldset>
</template>

<script setup lang="ts">
import { computed, reactive } from 'vue'
import { Button } from '@/components/ui/button'
import QuestionField from './QuestionField.vue'
import { visibleAnswers, visibleKeys } from './rules'
import type { FormQuestion } from './types'

// K6 · live phone preview. It renders the draft through the wizard's own field, so answering a
// question here reveals its follow-ups the way it will for a family.

const props = defineProps<{ questions: FormQuestion[]; program: string; session: string | null }>()

const camper = reactive<Record<string, string>>({})
const family = reactive<Record<string, string>>({})
const camperQuestions = computed(() => props.questions.filter((q) => q.scope === 'Participant'))
const familyQuestions = computed(() => props.questions.filter((q) => q.scope === 'Household'))
const familyShown = computed(() => visibleKeys(familyQuestions.value, family))
const camperShown = computed(() =>
  visibleKeys(camperQuestions.value, camper, visibleAnswers(familyQuestions.value, family)),
)
function reset() {
  for (const k of Object.keys(camper)) delete camper[k]
  for (const k of Object.keys(family)) delete family[k]
}
</script>

<template>
  <div class="mx-auto w-full max-w-[20rem] rounded-[2.5rem] border-[10px] border-foreground/90 bg-background shadow-lg">
    <div class="mx-auto mt-2 h-5 w-24 rounded-full bg-foreground/90" aria-hidden="true" />
    <div
      class="h-[34rem] space-y-5 overflow-y-auto px-4 pt-4 pb-6"
      role="region"
      aria-label="Phone preview"
      tabindex="0"
    >
      <div>
        <p class="text-xs text-muted-foreground">
          {{ program }}<template v-if="session"> · {{ session }}</template>
        </p>
        <p class="mt-1 text-lg font-semibold">Questions</p>
        <div class="mt-2 h-1.5 rounded-full bg-muted">
          <div class="h-1.5 w-1/3 rounded-full bg-primary" />
        </div>
      </div>
      <p v-if="!questions.length" class="text-sm text-muted-foreground">
        This form has no questions yet. Families skip straight to health.
      </p>
      <section v-if="camperQuestions.length" class="space-y-4" aria-label="Camper questions preview">
        <p class="font-medium">About your camper</p>
        <template v-for="q in camperQuestions" :key="q.key">
          <QuestionField
            v-if="camperShown.has(q.key)"
            v-model="camper[q.key]"
            :question="q"
            :field-id="`pv-${q.key}`"
            compact
          />
        </template>
      </section>
      <section v-if="familyQuestions.length" class="space-y-4" aria-label="Family questions preview">
        <p class="font-medium">About your family</p>
        <template v-for="q in familyQuestions" :key="q.key">
          <QuestionField
            v-if="familyShown.has(q.key)"
            v-model="family[q.key]"
            :question="q"
            :field-id="`pv-h-${q.key}`"
            compact
          />
        </template>
      </section>
      <div class="flex justify-between gap-2 pt-2">
        <Button variant="outline" size="sm" @click="reset">Clear answers</Button>
        <Button size="sm" disabled>Next</Button>
      </div>
    </div>
  </div>
</template>

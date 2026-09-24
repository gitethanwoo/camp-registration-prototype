<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { Skeleton } from '@/components/ui/skeleton'
import { api } from '@/lib/api'
import { answerText } from './rules'
import type { AnswerView, FamilyAnswers, StaffAnswers } from './types'

// Registration answers, labelled with the form version they were given on. Staff pages (C3) pass a
// registration id; the family's registration page (F5) passes the order's confirmation code.

const props = defineProps<{ registrationId?: number; confirmationCode?: string }>()

interface Group {
  title: string
  answers: AnswerView[]
}
const version = ref<number | null>(null)
const groups = ref<Group[] | null>(null)
const failed = ref(false)

onMounted(async () => {
  try {
    if (props.registrationId !== undefined) {
      const a = await api.get<StaffAnswers>(`/admin/forms/registrations/${props.registrationId}/answers`)
      version.value = a.formVersion
      groups.value = [
        { title: 'Camper', answers: a.participant },
        { title: 'Family', answers: a.household },
      ]
    } else if (props.confirmationCode) {
      const a = await api.get<FamilyAnswers>(`/family/forms/orders/${props.confirmationCode}/answers`)
      version.value = a.formVersion
      groups.value = [
        ...a.participants.map((p) => ({ title: p.firstName, answers: p.answers })),
        { title: 'Your family', answers: a.household },
      ]
    }
  } catch {
    failed.value = true
  }
})

const shown = computed(() => groups.value?.filter((g) => g.answers.length) ?? [])
</script>

<template>
  <div class="space-y-4 text-sm">
    <p v-if="failed" class="text-muted-foreground">Answers couldn't be loaded. Refresh to try again.</p>
    <Skeleton v-else-if="!groups" class="h-20" />
    <template v-else>
      <p class="text-muted-foreground">
        <template v-if="version">Answered on registration form v{{ version }}.</template>
        <template v-else>Answered before registration forms had versions.</template>
      </p>
      <p v-if="!shown.length" class="text-muted-foreground">No answers were given.</p>
      <section v-for="g in shown" :key="g.title" :aria-label="`${g.title} answers`" class="space-y-2">
        <h3 v-if="shown.length > 1" class="font-medium">{{ g.title }}</h3>
        <dl class="grid gap-x-6 gap-y-3 sm:grid-cols-2">
          <div v-for="a in g.answers" :key="a.key" :class="a.type === 'LongText' && 'sm:col-span-2'">
            <dt class="text-muted-foreground">{{ a.label }}</dt>
            <dd class="break-words whitespace-pre-line">{{ answerText(a) }}</dd>
          </div>
        </dl>
      </section>
    </template>
  </div>
</template>

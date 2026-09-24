<script setup lang="ts">
import { CircleCheck, Info, TriangleAlert } from '@lucide/vue'
import { computed, onMounted, ref } from 'vue'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Tabs, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { type Cabinmate, useActivityDraft } from './useActivityDraft'

// R5 · Cabinmate request (FR-25). Optional. Requests go to the rooming board (O3); they are never a room pick.
const props = defineProps<{ sessionId: number; campers: { id: number; firstName: string }[]; attempted: boolean }>()
const acts = useActivityDraft(props.sessionId)
const tab = ref(String(props.campers[0]?.id ?? ''))
const current = computed(() => props.campers.find((c) => String(c.id) === tab.value) ?? props.campers[0])
const limit = computed(() => acts.options.value?.cabinmateLimit ?? 2)
onMounted(() => {
  if (!acts.options.value) void acts.refresh(props.campers.map((c) => c.id))
})
function set(i: number, m: Cabinmate) {
  if (current.value) acts.setMate(current.value.id, i, m)
}
const match = (personId: number, i: number) => acts.matches[`${personId}:${i}`]
const incomplete = (m: { name: string; contact: string }) => props.attempted && !m.name.trim() !== !m.contact.trim()
</script>

<template>
  <Card v-if="current">
    <CardHeader>
      <CardTitle>Cabinmate requests</CardTitle>
      <CardDescription
        >Optional. Tell us who your camper would like to be with. We do our best to honor requests, but they aren't
        guaranteed.</CardDescription
      >
    </CardHeader>
    <CardContent class="space-y-5">
      <Tabs v-if="campers.length > 1" v-model="tab">
        <TabsList class="max-w-full overflow-x-auto">
          <TabsTrigger v-for="c in campers" :key="c.id" :value="String(c.id)" class="px-4">{{
            c.firstName
          }}</TabsTrigger>
        </TabsList>
      </Tabs>
      <div>
        <h3 class="font-semibold">{{ current.firstName }}'s requests</h3>
        <p class="text-sm text-muted-foreground">
          Up to {{ limit }} friends in this session. Enter each friend's full name and a parent's email address or the
          friend's confirmation code (it starts with WS-).
        </p>
      </div>
      <div class="space-y-4">
        <fieldset
          v-for="(m, i) in acts.mates(current.id)"
          :key="`${current.id}-${i}`"
          class="grid gap-3 sm:grid-cols-[1.5rem_1fr_1fr] sm:items-start"
        >
          <legend class="sr-only">Friend {{ i + 1 }}</legend>
          <span class="hidden pt-8 text-sm text-muted-foreground sm:block">{{ i + 1 }}</span>
          <div class="space-y-2">
            <Label :for="`mate-${current.id}-${i}-name`">Friend's full name</Label>
            <Input
              :id="`mate-${current.id}-${i}-name`"
              :model-value="m.name"
              autocomplete="off"
              :aria-invalid="(incomplete(m) && !m.name.trim()) || undefined"
              @update:model-value="(v) => set(i, { ...m, name: String(v) })"
              @blur="acts.checkMate(current.id, i)"
            />
          </div>
          <div class="space-y-2">
            <Label :for="`mate-${current.id}-${i}-contact`">Parent's email or friend code</Label>
            <Input
              :id="`mate-${current.id}-${i}-contact`"
              :model-value="m.contact"
              autocomplete="off"
              :aria-invalid="(incomplete(m) && !m.contact.trim()) || undefined"
              @update:model-value="(v) => set(i, { ...m, contact: String(v) })"
              @blur="acts.checkMate(current.id, i)"
            />
          </div>
          <p
            v-if="match(current.id, i) === true"
            class="flex items-start gap-2 text-sm text-emerald-800 sm:col-start-2 sm:col-end-4"
            role="status"
          >
            <CircleCheck class="mt-0.5 size-4 shrink-0" />We found {{ m.name.trim() }} in this session. We'll pass the
            request to the rooming team.
          </p>
          <p
            v-else-if="match(current.id, i) === false"
            class="flex items-start gap-2 rounded-md border border-amber-200 bg-amber-50 p-2 text-sm text-amber-900 sm:col-start-2 sm:col-end-4"
            role="status"
          >
            <TriangleAlert class="mt-0.5 size-4 shrink-0 text-amber-600" />Not matched yet. Check the spelling or the
            email or code. We'll keep the request and match it if {{ m.name.trim() || 'they' }} registers later.
          </p>
        </fieldset>
      </div>
      <div class="flex gap-3 rounded-lg border bg-muted/30 p-4 text-sm">
        <Info class="mt-0.5 size-4 shrink-0 text-muted-foreground" />
        <div class="space-y-1">
          <p class="font-medium">Mutual requests help, but cabins aren't guaranteed</p>
          <p class="text-muted-foreground">
            When two campers ask for each other, we're most likely to place them together. Cabins are set by grade and
            gender, and the rooming team weighs every request, so this isn't a room booking.
          </p>
        </div>
      </div>
    </CardContent>
  </Card>
</template>

<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue'
import { toast } from 'vue-sonner'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Sheet, SheetContent, SheetDescription, SheetFooter, SheetHeader, SheetTitle } from '@/components/ui/sheet'
import { Textarea } from '@/components/ui/textarea'
import { api } from '@/lib/api'
import { cn } from '@/lib/utils'
import PublishBadge from '@/features/setup/PublishBadge.vue'
import { saveError } from '@/features/setup/useSetupLoad'
import type { Catalog, CatalogActivity } from './types'

// K8 editor: create or edit one activity. The server audits every save with before and after values.
const props = defineProps<{ open: boolean; activity: CatalogActivity | null; catalog: Catalog | null }>()
const emit = defineEmits<{ close: []; saved: [id?: number] }>()

const grades = Array.from({ length: 12 }, (_, i) => String(i + 1))
const form = reactive({
  name: '',
  category: '',
  description: '',
  whatToBring: '',
  imageUrl: '',
  gradeMin: '3',
  gradeMax: '8',
  defaultCapacity: '24',
  staffRatio: '8',
  instructor: '',
  space: '',
  isActive: 'active',
})
const errors = ref<Record<string, string[]>>({})
const message = ref<string | null>(null)
const busy = ref(false)

watch(
  () => [props.open, props.activity?.id] as const,
  () => {
    const a = props.activity
    Object.assign(form, {
      name: a?.name ?? '',
      category: a?.category ?? '',
      description: a?.description ?? '',
      whatToBring: a?.whatToBring ?? '',
      imageUrl: a?.imageUrl ?? props.catalog?.images[0] ?? '',
      gradeMin: String(a?.gradeMin ?? 3),
      gradeMax: String(a?.gradeMax ?? 8),
      defaultCapacity: String(a?.defaultCapacity ?? 24),
      staffRatio: String(a?.staffRatio ?? 8),
      instructor: a?.instructor ?? '',
      space: a?.space ?? '',
      isActive: a && !a.isActive ? 'inactive' : 'active',
    })
    errors.value = {}
    message.value = null
  },
  { immediate: true },
)

const imageName = (url: string) => {
  const n = url.split('/').at(-1)?.replace('.svg', '') ?? ''
  return n.charAt(0).toUpperCase() + n.slice(1)
}
const body = computed(() => ({
  name: form.name,
  category: form.category,
  description: form.description,
  whatToBring: form.whatToBring,
  imageUrl: form.imageUrl,
  gradeMin: Number(form.gradeMin),
  gradeMax: Number(form.gradeMax),
  defaultCapacity: Number(form.defaultCapacity),
  staffRatio: Number(form.staffRatio),
  instructor: form.instructor,
  space: form.space,
  isActive: form.isActive === 'active',
}))

async function save() {
  busy.value = true
  errors.value = {}
  message.value = null
  try {
    if (props.activity) {
      await api.put(`/admin/setup/activities/${props.activity.id}`, body.value)
      toast.success(`${body.value.name} saved. The change is in the audit log.`)
      emit('saved', props.activity.id)
    } else {
      await api.post('/admin/setup/activities', body.value)
      toast.success(`${body.value.name} added to the catalog. Add it to a session's schedule to offer it.`)
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
const err = (k: string) => errors.value[k]?.[0]
</script>

<template>
  <Sheet :open="open" @update:open="(v) => !v && emit('close')">
    <SheetContent class="w-full overflow-y-auto sm:max-w-xl">
      <SheetHeader>
        <SheetTitle class="text-xl">{{ activity ? activity.name : 'New activity' }}</SheetTitle>
        <SheetDescription class="flex flex-wrap items-center gap-2">
          <template v-if="activity"
            ><PublishBadge :state="activity.isActive ? 'Active' : 'Inactive'" /> {{ activity.category }} · on
            {{ activity.upcomingSlots }} upcoming schedule
            {{ activity.upcomingSlots === 1 ? 'slot' : 'slots' }}</template
          >
          <template v-else>Families see it once it's on a session's schedule.</template>
        </SheetDescription>
      </SheetHeader>

      <form id="activity-form" class="grid gap-4 px-4 sm:grid-cols-2" @submit.prevent="save">
        <div class="space-y-2">
          <Label for="a-name">Activity name</Label>
          <Input id="a-name" v-model="form.name" :aria-invalid="!!err('name') || undefined" />
          <p v-if="err('name')" class="text-sm text-destructive">{{ err('name') }}</p>
        </div>
        <div class="space-y-2">
          <Label for="a-category">Category</Label>
          <Input
            id="a-category"
            v-model="form.category"
            list="a-categories"
            placeholder="Outdoor skills"
            :aria-invalid="!!err('category') || undefined"
          />
          <datalist id="a-categories">
            <option v-for="c in catalog?.categories ?? []" :key="c" :value="c" />
          </datalist>
          <p v-if="err('category')" class="text-sm text-destructive">{{ err('category') }}</p>
        </div>

        <fieldset class="space-y-2 sm:col-span-2">
          <legend class="text-sm font-medium">Photo</legend>
          <div class="grid grid-cols-4 gap-2 sm:grid-cols-7">
            <Button
              v-for="img in catalog?.images ?? []"
              :key="img"
              type="button"
              variant="outline"
              :aria-pressed="form.imageUrl === img"
              :aria-label="`${imageName(img)} photo`"
              :class="cn('h-auto p-0.5', form.imageUrl === img && 'ring-2 ring-primary')"
              @click="form.imageUrl = img"
            >
              <img :src="img" alt="" class="aspect-square w-full rounded object-cover" />
            </Button>
          </div>
          <p v-if="err('imageUrl')" class="text-sm text-destructive">{{ err('imageUrl') }}</p>
        </fieldset>

        <div class="space-y-2">
          <Label for="a-min">Lowest grade</Label>
          <Select v-model="form.gradeMin">
            <SelectTrigger id="a-min" class="w-full" :aria-invalid="!!err('gradeMin') || undefined"
              ><SelectValue
            /></SelectTrigger>
            <SelectContent>
              <SelectItem v-for="g in grades" :key="g" :value="g">Grade {{ g }}</SelectItem>
            </SelectContent>
          </Select>
        </div>
        <div class="space-y-2">
          <Label for="a-max">Highest grade</Label>
          <Select v-model="form.gradeMax">
            <SelectTrigger id="a-max" class="w-full"><SelectValue /></SelectTrigger>
            <SelectContent>
              <SelectItem v-for="g in grades" :key="g" :value="g">Grade {{ g }}</SelectItem>
            </SelectContent>
          </Select>
        </div>
        <p v-if="err('gradeMin')" class="text-sm text-destructive sm:col-span-2">{{ err('gradeMin') }}</p>

        <div class="space-y-2">
          <Label for="a-cap">Default capacity per period</Label>
          <Input
            id="a-cap"
            v-model="form.defaultCapacity"
            type="number"
            min="1"
            max="100"
            :aria-invalid="!!err('defaultCapacity') || undefined"
          />
          <p v-if="err('defaultCapacity')" class="text-sm text-destructive">{{ err('defaultCapacity') }}</p>
          <p v-else class="text-xs text-muted-foreground">
            Camper places in each period. Upcoming schedules still on the old default follow a change.
          </p>
        </div>
        <div class="space-y-2">
          <Label for="a-ratio">Staff ratio (campers per staff)</Label>
          <Input
            id="a-ratio"
            v-model="form.staffRatio"
            type="number"
            min="1"
            max="30"
            :aria-invalid="!!err('staffRatio') || undefined"
          />
          <p v-if="err('staffRatio')" class="text-sm text-destructive">{{ err('staffRatio') }}</p>
        </div>
        <div class="space-y-2">
          <Label for="a-instructor">Instructor requirement</Label>
          <Input
            id="a-instructor"
            v-model="form.instructor"
            placeholder="Certified archery instructor"
            :aria-invalid="!!err('instructor') || undefined"
          />
          <p v-if="err('instructor')" class="text-sm text-destructive">{{ err('instructor') }}</p>
        </div>
        <div class="space-y-2">
          <Label for="a-space">Space</Label>
          <Input
            id="a-space"
            v-model="form.space"
            placeholder="Archery range"
            :aria-invalid="!!err('space') || undefined"
          />
          <p v-if="err('space')" class="text-sm text-destructive">{{ err('space') }}</p>
        </div>
        <div class="space-y-2 sm:col-span-2">
          <Label for="a-desc">Family-facing description</Label>
          <Textarea id="a-desc" v-model="form.description" rows="4" :aria-invalid="!!err('description') || undefined" />
          <p v-if="err('description')" class="text-sm text-destructive">{{ err('description') }}</p>
        </div>
        <div class="space-y-2 sm:col-span-2">
          <Label for="a-bring">What to bring <span class="font-normal text-muted-foreground">(optional)</span></Label>
          <Input id="a-bring" v-model="form.whatToBring" :aria-invalid="!!err('whatToBring') || undefined" />
          <p v-if="err('whatToBring')" class="text-sm text-destructive">{{ err('whatToBring') }}</p>
        </div>
        <div class="space-y-2">
          <Label for="a-status">Status</Label>
          <Select v-model="form.isActive">
            <SelectTrigger id="a-status" class="w-full"><SelectValue /></SelectTrigger>
            <SelectContent>
              <SelectItem value="active">Active</SelectItem>
              <SelectItem value="inactive">Inactive (hidden from families)</SelectItem>
            </SelectContent>
          </Select>
        </div>
        <p v-if="message" class="text-sm text-destructive sm:col-span-2" role="alert">{{ message }}</p>
      </form>

      <SheetFooter class="flex-row flex-wrap gap-2">
        <Button type="submit" form="activity-form" :disabled="busy">{{
          activity ? 'Save activity' : 'Create activity'
        }}</Button>
        <Button variant="outline" @click="emit('close')">Cancel</Button>
      </SheetFooter>
    </SheetContent>
  </Sheet>
</template>

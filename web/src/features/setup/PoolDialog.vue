<script setup lang="ts">
import { reactive, ref, watch } from 'vue'
import { toast } from 'vue-sonner'
import { Button } from '@/components/ui/button'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { api } from '@/lib/api'
import type { SetupPool } from './types'
import { saveError } from './useSetupLoad'

// Adds a pool, or edits one. Who a pool is for locks once campers were placed by it; capacity can't go below seats taken.
const open = defineModel<boolean>('open', { required: true })
const props = defineProps<{ sessionId: number; pool: SetupPool | null }>()
const emit = defineEmits<{ saved: [] }>()

const form = reactive({ name: '', gender: 'any', gradeMin: '0', gradeMax: '12', capacity: '' })
const errors = ref<Record<string, string[]>>({})
const message = ref<string | null>(null)
const busy = ref(false)
watch(open, (v) => {
  if (!v) return
  const p = props.pool
  Object.assign(form, {
    name: p?.name ?? '',
    gender: p?.gender ?? 'any',
    gradeMin: String(p?.gradeMin ?? 0),
    gradeMax: String(p?.gradeMax ?? 12),
    capacity: p ? String(p.capacity) : '',
  })
  errors.value = {}
  message.value = null
})
const locked = () => !!props.pool && (props.pool.taken > 0 || props.pool.waitlisted > 0)

async function save() {
  busy.value = true
  errors.value = {}
  message.value = null
  const body = {
    name: form.name,
    gender: form.gender === 'any' ? null : form.gender,
    gradeMin: Number(form.gradeMin),
    gradeMax: Number(form.gradeMax),
    capacity: Number(form.capacity),
  }
  try {
    if (props.pool) await api.put(`/admin/setup/pools/${props.pool.id}`, body)
    else await api.post(`/admin/setup/sessions/${props.sessionId}/pools`, body)
    toast.success(
      props.pool
        ? `${form.name} saved. Capacity is ${form.capacity}.`
        : `${form.name} added with ${form.capacity} seats.`,
    )
    open.value = false
    emit('saved')
  } catch (e) {
    const err = saveError(e)
    message.value = err.message
    errors.value = err.fields
  } finally {
    busy.value = false
  }
}
const err = (k: string) => errors.value[k]?.[0]
</script>

<template>
  <Dialog v-model:open="open">
    <DialogContent class="sm:max-w-md">
      <DialogHeader>
        <DialogTitle>{{ pool ? `Edit ${pool.name}` : 'Add a capacity pool' }}</DialogTitle>
        <DialogDescription>
          Each camper lands in exactly one pool by grade and gender, so pools in a session can't overlap.
        </DialogDescription>
      </DialogHeader>
      <form id="pool-form" class="grid gap-4" @submit.prevent="save">
        <div class="space-y-2">
          <Label for="pool-name">Pool name</Label>
          <Input
            id="pool-name"
            v-model="form.name"
            placeholder="Boys G6–8"
            :aria-invalid="!!err('name') || undefined"
          />
          <p v-if="err('name')" class="text-sm text-destructive">{{ err('name') }}</p>
        </div>
        <div class="grid grid-cols-3 gap-3">
          <div class="col-span-3 space-y-2 sm:col-span-1">
            <Label for="pool-gender">Who</Label>
            <Select v-model="form.gender" :disabled="locked()">
              <SelectTrigger id="pool-gender" class="w-full"><SelectValue /></SelectTrigger>
              <SelectContent>
                <SelectItem value="any">Everyone</SelectItem>
                <SelectItem value="Male">Boys</SelectItem>
                <SelectItem value="Female">Girls</SelectItem>
              </SelectContent>
            </Select>
          </div>
          <div class="space-y-2">
            <Label for="pool-gmin">From grade <span class="font-normal text-muted-foreground">(0 = K)</span></Label>
            <Input id="pool-gmin" v-model="form.gradeMin" type="number" min="0" max="12" :disabled="locked()" />
          </div>
          <div class="space-y-2">
            <Label for="pool-gmax">To grade</Label>
            <Input id="pool-gmax" v-model="form.gradeMax" type="number" min="0" max="12" :disabled="locked()" />
          </div>
        </div>
        <p v-if="err('gradeMin') || err('gradeMax')" class="text-sm text-destructive">
          {{ err('gradeMin') ?? err('gradeMax') }}
        </p>
        <p v-else-if="locked()" class="text-xs text-muted-foreground">
          Campers are already placed in this pool by its grades, so only the name and capacity can change.
        </p>
        <div class="space-y-2">
          <Label for="pool-capacity">Capacity</Label>
          <Input
            id="pool-capacity"
            v-model="form.capacity"
            type="number"
            :min="pool?.taken ?? 0"
            :aria-invalid="!!err('capacity') || undefined"
          />
          <p v-if="err('capacity')" class="text-sm text-destructive">{{ err('capacity') }}</p>
          <p v-else-if="pool" class="text-xs text-muted-foreground">
            {{ pool.taken }} seats are taken, so the lowest it can go is {{ pool.taken }}.
          </p>
        </div>
      </form>
      <p v-if="message && !Object.keys(errors).length" class="text-sm text-destructive" role="alert">{{ message }}</p>
      <DialogFooter>
        <Button variant="outline" :disabled="busy" @click="open = false">Cancel</Button>
        <Button type="submit" form="pool-form" :disabled="busy || !form.name.trim() || form.capacity === ''">
          {{ pool ? 'Save pool' : 'Add pool' }}
        </Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>

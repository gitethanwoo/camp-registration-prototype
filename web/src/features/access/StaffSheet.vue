<script setup lang="ts">
import { Info, Lock } from '@lucide/vue'
import { computed, reactive, ref, watch } from 'vue'
import { toast } from 'vue-sonner'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Separator } from '@/components/ui/separator'
import { Sheet, SheetContent, SheetDescription, SheetFooter, SheetHeader, SheetTitle } from '@/components/ui/sheet'
import { Skeleton } from '@/components/ui/skeleton'
import { api } from '@/lib/api'
import { dateTime } from '@/lib/format'
import { saveError } from '@/features/setup/useSetupLoad'
import type { Ministry, StaffDetail } from './types'
import { actorName } from './when'

const props = defineProps<{ id: number | null; ministries: Ministry[] }>()
const emit = defineEmits<{ close: []; saved: [] }>()

const detail = ref<StaffDetail | null>(null)
const loadError = ref<string | null>(null)
const form = reactive({ ministryId: 'all', health: 'status' })
const message = ref<string | null>(null)
const fieldErrors = ref<Record<string, string[]>>({})
const busy = ref(false)

async function load(id: number) {
  loadError.value = null
  try {
    const d = await api.get<StaffDetail>(`/access/staff/${id}`)
    if (props.id !== id) return
    detail.value = d
    form.ministryId = d.member.ministry ? String(d.member.ministry.id) : 'all'
    form.health = d.member.healthAccess ? 'details' : 'status'
  } catch (e) {
    loadError.value = saveError(e).message
  }
}
watch(
  () => props.id,
  (id) => {
    detail.value = null
    message.value = null
    fieldErrors.value = {}
    if (id != null) void load(id)
  },
  { immediate: true },
)

const m = computed(() => detail.value?.member ?? null)
const revoked = computed(() => m.value?.status === 'Revoked')
const dirty = computed(() => {
  const x = m.value
  if (!x) return false
  return (
    form.ministryId !== (x.ministry ? String(x.ministry.id) : 'all') || (form.health === 'details') !== x.healthAccess
  )
})

async function save() {
  const x = m.value
  if (!x) return
  busy.value = true
  message.value = null
  fieldErrors.value = {}
  try {
    await api.put(`/access/staff/${x.id}`, {
      ministryId: form.ministryId === 'all' ? null : Number(form.ministryId),
      healthAccess: form.health === 'details',
    })
    toast.success(`${x.name}'s access saved.`)
    emit('saved')
    await load(x.id)
  } catch (e) {
    const err = saveError(e)
    message.value = err.message
    fieldErrors.value = err.fields
  } finally {
    busy.value = false
  }
}
const err = (k: string) => fieldErrors.value[k]?.[0]
</script>

<template>
  <Sheet :open="id != null" @update:open="(v) => !v && emit('close')">
    <SheetContent class="w-full overflow-y-auto sm:max-w-lg">
      <SheetHeader>
        <SheetTitle class="text-xl">{{ m?.name ?? 'Staff member' }}</SheetTitle>
        <SheetDescription class="break-all">{{ m?.email }}</SheetDescription>
      </SheetHeader>

      <div v-if="loadError" class="px-4">
        <Alert variant="destructive"
          ><AlertDescription>{{ loadError }}</AlertDescription></Alert
        >
      </div>
      <div v-else-if="!m" class="space-y-3 px-4" aria-busy="true">
        <Skeleton class="h-10 w-full" />
        <Skeleton class="h-10 w-full" />
        <Skeleton class="h-24 w-full" />
      </div>
      <div v-else class="space-y-5 px-4">
        <Alert v-if="revoked">
          <Lock />
          <AlertDescription>
            {{ m.name }} is no longer in the WinShape Staff organization in WorkOS, so they can't sign in or see
            anything here. Add them back in WorkOS to restore access.
          </AlertDescription>
        </Alert>

        <dl class="grid grid-cols-2 gap-x-4 gap-y-3 text-sm">
          <div>
            <dt class="text-muted-foreground">Role</dt>
            <dd class="font-medium">{{ m.roleLabel }}</dd>
            <dd class="text-muted-foreground">Set in WorkOS</dd>
          </div>
          <div>
            <dt class="text-muted-foreground">Status</dt>
            <dd>
              <Badge
                variant="outline"
                :class="
                  revoked
                    ? 'border-transparent bg-muted text-muted-foreground'
                    : 'border-emerald-200 bg-emerald-50 text-emerald-800'
                "
                >{{ m.status }}</Badge
              >
            </dd>
          </div>
          <div>
            <dt class="text-muted-foreground">Last sign-in</dt>
            <dd>{{ m.lastSignInAt ? dateTime(m.lastSignInAt) : 'Never' }}</dd>
          </div>
          <div>
            <dt class="text-muted-foreground">{{ revoked ? 'Revoked' : 'Last synced' }}</dt>
            <dd>
              {{ revoked && m.revokedAt ? dateTime(m.revokedAt) : m.syncedAt ? dateTime(m.syncedAt) : 'Not yet' }}
            </dd>
          </div>
        </dl>

        <Separator />

        <form id="staff-form" class="space-y-4" @submit.prevent="save">
          <div class="space-y-2">
            <Label for="s-scope">Ministry scope</Label>
            <Select v-model="form.ministryId" :disabled="revoked">
              <SelectTrigger id="s-scope" class="w-full" :aria-invalid="!!err('ministryId') || undefined"
                ><SelectValue
              /></SelectTrigger>
              <SelectContent>
                <SelectItem value="all">All ministries</SelectItem>
                <SelectItem v-for="x in ministries" :key="x.id" :value="String(x.id)">{{ x.name }}</SelectItem>
              </SelectContent>
            </Select>
            <p class="text-sm text-muted-foreground">
              Health details are limited to this ministry's programs. Registrations and households aren't scoped yet.
            </p>
            <p v-if="err('ministryId')" class="text-sm text-destructive">{{ err('ministryId') }}</p>
          </div>
          <div class="space-y-2">
            <Label for="s-health">Health data</Label>
            <Select v-model="form.health" :disabled="revoked || !m.canViewHealth">
              <SelectTrigger id="s-health" class="w-full" :aria-invalid="!!err('healthAccess') || undefined"
                ><SelectValue
              /></SelectTrigger>
              <SelectContent>
                <SelectItem value="details">Health details</SelectItem>
                <SelectItem value="status">Completion status only</SelectItem>
              </SelectContent>
            </Select>
            <p class="text-sm text-muted-foreground">
              <template v-if="!m.canViewHealth">{{ m.roleLabel }}s never see camper health details.</template>
              <template v-else
                >Health details also need the program to allow {{ m.roleLabel }} under Setup › Health settings. Every
                view is recorded in the audit log.</template
              >
            </p>
            <p v-if="err('healthAccess')" class="text-sm text-destructive">{{ err('healthAccess') }}</p>
          </div>
        </form>

        <Alert v-if="message" variant="destructive"
          ><AlertDescription>{{ message }}</AlertDescription></Alert
        >

        <section class="space-y-2">
          <h3 class="text-sm font-medium">Can view health details for</h3>
          <ul v-if="detail?.healthPrograms.length" class="space-y-1 text-sm">
            <li v-for="p in detail.healthPrograms" :key="p.id">{{ p.name }}</li>
          </ul>
          <p v-else class="text-sm text-muted-foreground">No programs right now.</p>
        </section>

        <Alert>
          <Info />
          <AlertDescription>
            People, names and roles come from WorkOS. To add someone or change a role, change it there and select Sync
            now.
          </AlertDescription>
        </Alert>

        <section class="space-y-2">
          <h3 class="text-sm font-medium">Recent activity</h3>
          <ol v-if="detail?.activity.length" class="space-y-3 text-sm">
            <li v-for="a in detail.activity" :key="a.id">
              <div>{{ a.detail }}</div>
              <div class="text-muted-foreground">{{ dateTime(a.createdAt) }} · {{ actorName(a.actor) }}</div>
            </li>
          </ol>
          <p v-else class="text-sm text-muted-foreground">No changes recorded yet.</p>
        </section>
      </div>

      <SheetFooter v-if="m && !revoked" class="flex-row items-center justify-end gap-2 border-t">
        <span v-if="dirty" class="mr-auto text-sm text-muted-foreground">Unsaved changes</span>
        <Button variant="outline" @click="emit('close')">Close</Button>
        <Button type="submit" form="staff-form" :disabled="!dirty || busy">{{
          busy ? 'Saving…' : 'Save access'
        }}</Button>
      </SheetFooter>
    </SheetContent>
  </Sheet>
</template>

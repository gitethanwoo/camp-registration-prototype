<script setup lang="ts">
import { Ellipsis, Info, Lock, Plus, TriangleAlert } from '@lucide/vue'
import type { ColumnDef } from '@tanstack/vue-table'
import { computed, onMounted, reactive, ref, watch } from 'vue'
import { RouterLink } from 'vue-router'
import { toast } from 'vue-sonner'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import {
  AlertDialog,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from '@/components/ui/alert-dialog'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { DataTable } from '@/components/ui/data-table'
import { DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger } from '@/components/ui/dropdown-menu'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Skeleton } from '@/components/ui/skeleton'
import { useAdminScope } from '@/composables/useAdminScope'
import { api } from '@/lib/api'
import { dateRange } from '@/lib/format'
import AdminOnly from './AdminOnly.vue'
import PoolDialog from './PoolDialog.vue'
import type { SessionSetup, SetupPool } from './types'
import { saveError, useSetupLoad } from './useSetupLoad'

// K3 · Session editor: details, registration dates and capacity pools (FR-14, FR-69).
const props = defineProps<{ id?: number }>()
const scope = useAdminScope()
const sessionId = computed(() => props.id ?? scope.sessionId.value)
const {
  data: s,
  forbidden,
  error,
  load,
} = useSetupLoad<SessionSetup>(() => (sessionId.value ? `/admin/setup/sessions/${sessionId.value}` : null))
onMounted(async () => {
  await scope.ready
  await load()
})
watch(sessionId, load)

// Datetimes are camp wall-clock times; the input shows the first 16 characters ("2028-03-01T09:00").
const local = (v: string | null) => (v ? v.slice(0, 16) : '')
const form = reactive({
  name: '',
  startDate: '',
  endDate: '',
  location: '',
  registrationOpensAt: '',
  priorityOpensAt: '',
})
function reset() {
  if (!s.value) return
  Object.assign(form, {
    name: s.value.name,
    startDate: s.value.startDate,
    endDate: s.value.endDate,
    location: s.value.location,
    registrationOpensAt: local(s.value.registrationOpensAt),
    priorityOpensAt: local(s.value.priorityOpensAt),
  })
}
watch(s, reset)
const dirty = computed(
  () =>
    !!s.value &&
    (form.name !== s.value.name ||
      form.startDate !== s.value.startDate ||
      form.endDate !== s.value.endDate ||
      form.location !== s.value.location ||
      form.registrationOpensAt !== local(s.value.registrationOpensAt) ||
      form.priorityOpensAt !== local(s.value.priorityOpensAt)),
)
const errors = ref<Record<string, string[]>>({})
const saving = ref(false)
async function save() {
  saving.value = true
  errors.value = {}
  try {
    await api.put(`/admin/setup/sessions/${sessionId.value}`, {
      ...form,
      registrationOpensAt: form.registrationOpensAt || null,
      priorityOpensAt: form.priorityOpensAt || null,
    })
    toast.success(`${form.name} saved.`)
    await load()
  } catch (e) {
    const err = saveError(e)
    errors.value = Object.keys(err.fields).length ? err.fields : { form: [err.message] }
  } finally {
    saving.value = false
  }
}
const err = (k: string) => errors.value[k]?.[0]

const statusTone: Record<string, string> = {
  'Registration open': 'border-emerald-200 bg-emerald-50 text-emerald-800',
}

const columns: ColumnDef<SetupPool>[] = [
  { accessorKey: 'name', header: 'Pool', meta: { cellClass: 'whitespace-normal font-medium' } },
  { id: 'who', header: 'Who', meta: { class: 'hidden sm:table-cell', cellClass: 'text-muted-foreground' } },
  { accessorKey: 'capacity', header: 'Capacity', meta: { class: 'text-right', cellClass: 'text-right tabular-nums' } },
  {
    accessorKey: 'taken',
    header: 'Taken',
    meta: { class: 'hidden text-right sm:table-cell', cellClass: 'text-right tabular-nums' },
  },
  {
    accessorKey: 'open',
    header: 'Open',
    meta: { class: 'hidden text-right sm:table-cell', cellClass: 'hidden text-right tabular-nums sm:table-cell' },
  },
  {
    accessorKey: 'waitlisted',
    header: 'Waitlist',
    meta: { class: 'hidden text-right sm:table-cell', cellClass: 'text-right tabular-nums' },
  },
  { id: 'actions', header: '', meta: { class: 'w-10' } },
]
const grade = (n: number) => (n === 0 ? 'K' : String(n))
function who(p: SetupPool) {
  let g = `grades ${grade(p.gradeMin)}–${grade(p.gradeMax)}`
  if (p.gradeMin >= 99) g = 'adults'
  else if (p.gradeMin === p.gradeMax) g = `grade ${grade(p.gradeMin)}`
  return p.gender ? `${p.gender === 'Male' ? 'Boys' : 'Girls'}, ${g}` : `Everyone, ${g}`
}

const poolOpen = ref(false)
const editing = ref<SetupPool | null>(null)
function openPool(p: SetupPool | null) {
  editing.value = p
  poolOpen.value = true
}
const removing = ref<SetupPool | null>(null)
async function remove() {
  const p = removing.value
  if (!p) return
  try {
    await api.delete(`/admin/setup/pools/${p.id}`)
    toast.success(`${p.name} removed.`)
    await load()
  } catch (e) {
    toast.error(saveError(e).message)
  } finally {
    removing.value = null
  }
}
</script>

<template>
  <AdminOnly v-if="forbidden" page="Session setup" />
  <Alert v-else-if="error" variant="destructive" class="mx-auto max-w-6xl"
    ><AlertDescription>{{ error }}</AlertDescription></Alert
  >
  <Skeleton v-else-if="!s" class="mx-auto h-96 max-w-6xl rounded-xl" />
  <div v-else class="mx-auto max-w-6xl space-y-6">
    <div class="flex flex-wrap items-start justify-between gap-4">
      <div class="min-w-0">
        <div class="flex flex-wrap items-center gap-3">
          <h1 class="text-2xl font-semibold tracking-tight">{{ s.program.name }} · {{ s.name }}</h1>
          <Badge variant="outline" :class="statusTone[s.status] ?? 'bg-muted text-muted-foreground'">{{
            s.status
          }}</Badge>
        </div>
        <p class="text-muted-foreground">
          {{ dateRange(s.startDate, s.endDate) }} · {{ s.program.ministry }} · {{ s.location }}
        </p>
      </div>
      <div class="flex items-center gap-3">
        <span v-if="dirty" class="flex items-center gap-1.5 text-sm text-amber-700"
          ><span class="size-2 rounded-full bg-amber-500" aria-hidden="true" />Unsaved changes</span
        >
        <Button v-if="dirty" variant="outline" :disabled="saving" @click="reset">Discard</Button>
        <Button :disabled="saving || !dirty" @click="save">{{ saving ? 'Saving…' : 'Save changes' }}</Button>
      </div>
    </div>

    <Alert v-for="name in s.fullPools" :key="name" class="border-amber-300 bg-amber-50 text-amber-900">
      <TriangleAlert />
      <AlertTitle class="line-clamp-none">{{ name }} is full</AlertTitle>
      <AlertDescription class="text-amber-900"
        >Families are on its waitlist. Raising the capacity doesn't offer anyone a spot; an admin offers each spot from
        the waitlist.
        <RouterLink to="/admin/waitlist" class="underline underline-offset-4"
          >Open the waitlist</RouterLink
        ></AlertDescription
      >
    </Alert>
    <Alert v-if="s.program.state !== 'Published'">
      <Info />
      <AlertDescription>
        {{ s.program.name }} is {{ s.program.stateLabel.toLowerCase() }}, so families can't register yet.
        <RouterLink to="/admin/setup/programs" class="underline underline-offset-4">Open programs</RouterLink>
      </AlertDescription>
    </Alert>

    <div class="grid gap-6 lg:grid-cols-[1fr_22rem]">
      <div class="min-w-0 space-y-6">
        <Card>
          <CardHeader class="flex flex-row items-start justify-between gap-3">
            <div>
              <CardTitle>Capacity pools</CardTitle>
              <CardDescription>Capacity can't drop below seats already taken.</CardDescription>
            </div>
            <Button variant="outline" size="sm" :disabled="s.capacityFromOpera" @click="openPool(null)"
              ><Plus />Add pool</Button
            >
          </CardHeader>
          <CardContent>
            <DataTable :columns="columns" :data="s.pools" :get-row-id="(p) => String(p.id)" empty-text="No pools yet.">
              <template #cell-name="{ row: p }">
                {{ p.name }}
                <div class="text-xs font-normal text-muted-foreground sm:hidden">
                  {{ who(p) }}<br />{{ p.taken }} taken · {{ p.waitlisted }} waiting
                </div>
              </template>
              <template #cell-who="{ row: p }">{{ who(p) }}</template>
              <template #cell-actions="{ row: p }">
                <DropdownMenu>
                  <DropdownMenuTrigger as-child>
                    <Button variant="ghost" size="icon" :aria-label="`Actions for ${p.name}`"><Ellipsis /></Button>
                  </DropdownMenuTrigger>
                  <DropdownMenuContent align="end">
                    <DropdownMenuItem :disabled="s.capacityFromOpera" @select="openPool(p)">Edit pool</DropdownMenuItem>
                    <DropdownMenuItem :disabled="!p.removable" class="text-destructive" @select="removing = p"
                      >Remove pool</DropdownMenuItem
                    >
                  </DropdownMenuContent>
                </DropdownMenu>
              </template>
            </DataTable>
            <p class="mt-3 text-right text-sm text-muted-foreground tabular-nums">
              Total: {{ s.totals.taken }} of {{ s.totals.capacity }} taken · {{ s.totals.open }} open ·
              {{ s.totals.waitlisted }} waiting
            </p>
          </CardContent>
        </Card>

        <div class="grid grid-cols-2 gap-3 sm:grid-cols-4">
          <div
            v-for="[label, value] in [
              ['Total capacity', s.totals.capacity],
              ['Registered', s.registered],
              ['Open spots', s.totals.open],
              ['On waitlist', s.totals.waitlisted],
            ]"
            :key="label"
            class="rounded-xl border p-4"
          >
            <p class="text-2xl font-semibold tabular-nums">{{ value }}</p>
            <p class="text-sm text-muted-foreground">{{ label }}</p>
          </div>
        </div>
      </div>

      <Card>
        <CardHeader><CardTitle>Session details</CardTitle></CardHeader>
        <CardContent>
          <form class="space-y-4" @submit.prevent="save">
            <div class="space-y-2">
              <Label for="s-name">Name</Label>
              <Input id="s-name" v-model="form.name" :aria-invalid="!!err('name') || undefined" />
              <p v-if="err('name')" class="text-sm text-destructive">{{ err('name') }}</p>
            </div>
            <div class="grid grid-cols-2 gap-3">
              <div class="space-y-2">
                <Label for="s-start">Starts</Label>
                <Input id="s-start" v-model="form.startDate" type="date" />
              </div>
              <div class="space-y-2">
                <Label for="s-end">Ends</Label>
                <Input id="s-end" v-model="form.endDate" type="date" />
              </div>
            </div>
            <p v-if="err('startDate') || err('endDate')" class="text-sm text-destructive">
              {{ err('startDate') ?? err('endDate') }}
            </p>
            <div class="space-y-2">
              <Label for="s-location">Location</Label>
              <Select v-model="form.location">
                <SelectTrigger id="s-location" class="w-full"><SelectValue /></SelectTrigger>
                <SelectContent>
                  <SelectItem v-for="l in s.locations" :key="l" :value="l">{{ l }}</SelectItem>
                </SelectContent>
              </Select>
            </div>
            <Alert v-if="s.capacityFromOpera">
              <Info />
              <AlertTitle class="line-clamp-none">Room capacity comes from Oracle Opera</AlertTitle>
              <AlertDescription>Retreat-center pools are read-only here.</AlertDescription>
            </Alert>
            <div class="space-y-2">
              <Label for="s-open">Registration opens</Label>
              <Input
                id="s-open"
                v-model="form.registrationOpensAt"
                type="datetime-local"
                :aria-invalid="!!err('registrationOpensAt') || undefined"
              />
              <p v-if="err('registrationOpensAt')" class="text-sm text-destructive">{{ err('registrationOpensAt') }}</p>
            </div>
            <div class="space-y-2">
              <Label for="s-priority"
                >Priority registration <span class="font-normal text-muted-foreground">(optional)</span></Label
              >
              <Input
                id="s-priority"
                v-model="form.priorityOpensAt"
                type="datetime-local"
                :aria-invalid="!!err('priorityOpensAt') || undefined"
              />
              <p v-if="err('priorityOpensAt')" class="text-sm text-destructive">{{ err('priorityOpensAt') }}</p>
              <p v-else class="text-xs text-muted-foreground">
                Shown to staff only for now. Checkout doesn't hold families to either date yet.
              </p>
            </div>
            <div class="space-y-2">
              <Label for="s-waitlist">Waitlist mode</Label>
              <div
                id="s-waitlist"
                class="flex h-9 items-center justify-between rounded-md border bg-muted/40 px-3 text-sm"
              >
                {{ s.waitlistModeLabel }} <Lock class="size-4 text-muted-foreground" aria-label="Locked" />
              </div>
              <p class="text-xs text-muted-foreground">Locked in v1: staff offer each open spot in waitlist order.</p>
            </div>
            <p v-if="err('form')" class="text-sm text-destructive" role="alert">{{ err('form') }}</p>
          </form>
        </CardContent>
      </Card>
    </div>

    <PoolDialog v-model:open="poolOpen" :session-id="s.id" :pool="editing" @saved="load" />
    <AlertDialog :open="!!removing" @update:open="(v) => !v && (removing = null)">
      <AlertDialogContent v-if="removing">
        <AlertDialogHeader>
          <AlertDialogTitle>Remove {{ removing.name }}?</AlertDialogTitle>
          <AlertDialogDescription
            >It has no campers or waitlist. Its {{ removing.capacity }} seats come off the
            session.</AlertDialogDescription
          >
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel>Keep it</AlertDialogCancel>
          <Button variant="destructive" @click="remove">Remove pool</Button>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  </div>
</template>

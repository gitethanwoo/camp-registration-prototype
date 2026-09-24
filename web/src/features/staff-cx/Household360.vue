<script setup lang="ts">
import { AlertTriangle, ArrowLeft, ExternalLink, Lock, Mail, Phone, UserRound } from '@lucide/vue'
import type { ColumnDef } from '@tanstack/vue-table'
import { computed, onMounted, ref, watch } from 'vue'
import { RouterLink } from 'vue-router'
import { toast } from 'vue-sonner'
import StatusBadge from '@/components/StatusBadge.vue'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Checkbox } from '@/components/ui/checkbox'
import { DataTable } from '@/components/ui/data-table'
import { Label } from '@/components/ui/label'
import { Skeleton } from '@/components/ui/skeleton'
import { Textarea } from '@/components/ui/textarea'
import { api, ApiError } from '@/lib/api'
import { dateRange, date, dateTime, money } from '@/lib/format'
import { useSession } from '@/lib/session'
import PaymentBadge from './PaymentBadge.vue'
import type { HouseholdDetail, HouseholdNote } from './types'

// C2 · Household 360: everything about one family across ministries. Health details never load here.
const props = defineProps<{ id: number }>()
const h = ref<HouseholdDetail | null>(null)
const loadError = ref<string | null>(null)
const { session } = useSession()
const canVerify = computed(() => ['cet', 'admin'].includes(session.value?.role ?? ''))

async function load() {
  try {
    h.value = await api.get<HouseholdDetail>(`/admin/households/${props.id}`)
    loadError.value = null
  } catch (e) {
    loadError.value =
      e instanceof ApiError && e.status === 404 ? 'No household with that id.' : "Couldn't load this household."
  }
}
onMounted(load)
watch(() => props.id, load)

type Reg = HouseholdDetail['registrations'][number]
const regColumns: ColumnDef<Reg>[] = [
  { id: 'camp', header: 'Camp', meta: { cellClass: 'align-top whitespace-normal' } },
  {
    accessorKey: 'participant',
    header: 'Camper',
    meta: { class: 'hidden sm:table-cell', cellClass: 'align-top font-medium' },
  },
  { accessorKey: 'status', header: 'Status', meta: { cellClass: 'align-top' } },
  { id: 'payment', header: 'Payment', meta: { class: 'hidden md:table-cell', cellClass: 'align-top' } },
  {
    id: 'amount',
    header: 'Amount',
    cell: ({ row }) => money(row.original.priceCents - row.original.discountCents),
    meta: { class: 'hidden md:table-cell text-right', cellClass: 'align-top tabular-nums' },
  },
  {
    accessorKey: 'balanceCents',
    header: 'Balance',
    cell: ({ row }) => money(row.original.balanceCents),
    meta: { class: 'text-right', cellClass: 'align-top tabular-nums' },
  },
]
type Wait = HouseholdDetail['waitlist'][number]
const waitColumns: ColumnDef<Wait>[] = [
  { accessorKey: 'participant', header: 'Camper', meta: { cellClass: 'font-medium' } },
  {
    id: 'where',
    header: 'Session',
    cell: ({ row }) => `${row.original.program} · ${row.original.session} · ${row.original.pool}`,
  },
  { accessorKey: 'position', header: '#', meta: { cellClass: 'tabular-nums' } },
  { accessorKey: 'status', header: 'Status' },
]
type History = HouseholdDetail['history'][number]
function salesforceLine(sf: { id: string | null; status: string; lastSyncAt: string | null }) {
  const status = sf.lastSyncAt ? `${sf.status} ${dateTime(sf.lastSyncAt)}` : sf.status
  return sf.id ? `${sf.id} · ${status}` : status
}
const historyColumns: ColumnDef<History>[] = [
  {
    accessorKey: 'createdAt',
    header: 'When',
    cell: ({ row }) => dateTime(row.original.createdAt),
    meta: { cellClass: 'whitespace-nowrap align-top text-muted-foreground' },
  },
  { accessorKey: 'detail', header: 'What happened', meta: { cellClass: 'align-top whitespace-normal' } },
  {
    accessorKey: 'actor',
    header: 'By',
    meta: { class: 'hidden md:table-cell', cellClass: 'align-top text-muted-foreground' },
  },
]

// ── Notes ──
const note = ref('')
const savingNote = ref(false)
const noteError = ref<string | null>(null)
async function addNote() {
  if (!h.value) return
  if (!note.value.trim()) {
    noteError.value = 'Write the note first.'
    return
  }
  savingNote.value = true
  noteError.value = null
  try {
    await api.post<HouseholdNote>(`/admin/households/${h.value.id}/notes`, { body: note.value })
    note.value = ''
    toast.success('Note added.')
    await load()
  } catch (e) {
    noteError.value = e instanceof ApiError ? e.message : "The note didn't save. Try again."
  } finally {
    savingNote.value = false
  }
}

// ── Verification checklist ──
const verified = computed(() => h.value?.verification.filter((v) => v.checked).length ?? 0)
async function toggle(key: string, checked: boolean) {
  if (!h.value) return
  try {
    await api.put(`/admin/households/${h.value.id}/verification/${key}`, { checked })
    await load()
  } catch (e) {
    toast.error(e instanceof ApiError ? e.message : "That didn't save. Try again.")
  }
}

const primary = computed(() => h.value?.adults.find((a) => a.role === 'Primary') ?? h.value?.adults[0])
</script>

<template>
  <div class="mx-auto max-w-7xl space-y-6">
    <Button variant="ghost" size="sm" as-child class="-ml-2.5 text-muted-foreground">
      <RouterLink to="/admin/search"><ArrowLeft />Search families</RouterLink>
    </Button>
    <Alert v-if="loadError" variant="destructive"
      ><AlertDescription>{{ loadError }}</AlertDescription></Alert
    >
    <Skeleton v-else-if="!h" class="h-96 rounded-xl" />
    <template v-else>
      <header class="space-y-2">
        <p class="text-sm text-muted-foreground">Household #{{ h.id }}</p>
        <h1 class="text-2xl font-semibold tracking-tight md:text-3xl">{{ h.name }} household</h1>
        <div class="flex flex-wrap items-center gap-2">
          <Badge v-if="h.mergedIntoHouseholdId" variant="outline" class="bg-muted text-muted-foreground">Merged</Badge>
          <Button
            v-if="h.duplicates.length"
            variant="outline"
            size="xs"
            as-child
            class="border-amber-300 bg-amber-50 text-amber-900"
          >
            <RouterLink :to="`/admin/duplicates/${h.duplicates[0]!.householdA}/${h.duplicates[0]!.householdB}`"
              ><AlertTriangle />Possible duplicate</RouterLink
            >
          </Button>
          <span class="text-sm text-muted-foreground"> Salesforce: {{ salesforceLine(h.salesforce) }} </span>
        </div>
      </header>

      <Alert v-if="h.mergedIntoHouseholdId">
        <AlertTriangle />
        <AlertTitle class="line-clamp-none">This account was merged</AlertTitle>
        <AlertDescription>
          <p>
            Everything moved to
            <RouterLink :to="`/admin/households/${h.mergedIntoHouseholdId}`" class="underline"
              >household #{{ h.mergedIntoHouseholdId }}</RouterLink
            >. Make changes there.
          </p>
        </AlertDescription>
      </Alert>

      <div class="grid gap-6 lg:grid-cols-[minmax(0,1fr)_22rem]">
        <div class="min-w-0 space-y-6">
          <div class="grid gap-4 md:grid-cols-2">
            <Card>
              <CardHeader><CardTitle class="text-base">Adults with access</CardTitle></CardHeader>
              <CardContent class="space-y-4">
                <div v-for="a in h.adults" :key="a.id" class="flex gap-3">
                  <UserRound class="mt-0.5 size-5 shrink-0 text-muted-foreground" />
                  <div class="min-w-0">
                    <div class="font-medium">{{ a.name }}</div>
                    <div class="text-sm text-muted-foreground">
                      {{ a.role === 'Primary' ? 'Primary contact' : a.role }}
                    </div>
                    <div v-if="a.email" class="truncate text-sm">{{ a.email }}</div>
                  </div>
                </div>
                <p v-if="!h.adults.length" class="text-sm text-muted-foreground">No adults on file.</p>
              </CardContent>
            </Card>
            <Card>
              <CardHeader
                ><CardTitle class="text-base">Children ({{ h.children.length }})</CardTitle></CardHeader
              >
              <CardContent>
                <dl class="space-y-3">
                  <div v-for="c in h.children" :key="c.id" class="flex flex-wrap justify-between gap-x-4">
                    <dt class="font-medium">{{ c.name }}</dt>
                    <dd class="text-sm text-muted-foreground">
                      DOB {{ date(c.dateOfBirth) }} · {{ c.grade }} (fall {{ c.gradeYear }})
                    </dd>
                  </div>
                </dl>
                <p v-if="!h.children.length" class="text-sm text-muted-foreground">No children on file.</p>
              </CardContent>
            </Card>
          </div>

          <Card>
            <CardHeader>
              <CardTitle>Registrations</CardTitle>
              <CardDescription>Every ministry, newest session first.</CardDescription>
            </CardHeader>
            <CardContent class="space-y-3">
              <DataTable
                :columns="regColumns"
                :data="h.registrations"
                :get-row-id="(r) => String(r.id)"
                empty-text="No registrations yet."
              >
                <template #cell-camp="{ row: r }">
                  <RouterLink :to="`/admin/registrations/${r.id}`" class="font-medium hover:underline">{{
                    r.program
                  }}</RouterLink>
                  <div class="text-sm text-muted-foreground">
                    {{ r.ministry }} · {{ dateRange(r.startDate, r.endDate) }} · {{ r.pool }}
                  </div>
                  <div class="text-sm sm:hidden">{{ r.participant }}</div>
                  <div v-if="r.confirmationCode" class="text-xs text-muted-foreground">{{ r.confirmationCode }}</div>
                </template>
                <template #cell-status="{ row: r }"><StatusBadge :status="r.status" /></template>
                <template #cell-payment="{ row: r }"><PaymentBadge :status="r.payment" /></template>
              </DataTable>
              <div
                v-if="h.registrations.length"
                class="flex flex-wrap justify-end gap-x-6 gap-y-1 border-t pt-3 text-sm"
              >
                <span
                  >Household total <strong class="tabular-nums">{{ money(h.totals.priceCents) }}</strong></span
                >
                <span
                  >Paid <strong class="tabular-nums">{{ money(h.totals.paidCents) }}</strong></span
                >
                <span
                  >Balance <strong class="tabular-nums">{{ money(h.totals.balanceCents) }}</strong></span
                >
              </div>
            </CardContent>
          </Card>

          <Card v-if="h.waitlist.length">
            <CardHeader><CardTitle>Waitlist</CardTitle></CardHeader>
            <CardContent>
              <DataTable :columns="waitColumns" :data="h.waitlist" :get-row-id="(w) => String(w.id)">
                <template #cell-status="{ row: w }"><StatusBadge :status="w.status" /></template>
              </DataTable>
            </CardContent>
          </Card>

          <Card v-if="h.transfers.length">
            <CardHeader><CardTitle>Transfer requests</CardTitle></CardHeader>
            <CardContent>
              <ul class="divide-y">
                <li
                  v-for="t in h.transfers"
                  :key="t.id"
                  class="flex flex-wrap items-center justify-between gap-2 py-2 first:pt-0 last:pb-0"
                >
                  <span>{{ t.participant }}: {{ t.from }} → {{ t.to }}</span>
                  <span class="flex items-center gap-2 text-sm text-muted-foreground">
                    {{ date(t.createdAt) }}
                    <Badge variant="outline">{{ t.status }}</Badge>
                  </span>
                </li>
              </ul>
            </CardContent>
          </Card>

          <Card>
            <CardHeader><CardTitle>History</CardTitle></CardHeader>
            <CardContent>
              <DataTable
                :columns="historyColumns"
                :data="h.history"
                empty-text="Nothing recorded for this household yet."
              />
            </CardContent>
          </Card>
        </div>

        <aside class="min-w-0 space-y-6">
          <Card>
            <CardHeader><CardTitle class="text-base">Contact</CardTitle></CardHeader>
            <CardContent class="space-y-2 text-sm">
              <div v-if="primary" class="font-medium">{{ primary.name }}</div>
              <div class="flex items-center gap-2">
                <Mail class="size-4 text-muted-foreground" /><span class="truncate">{{ h.email }}</span>
              </div>
              <div class="flex items-center gap-2">
                <Phone class="size-4 text-muted-foreground" /><span class="tabular-nums">{{ h.phone || '—' }}</span>
              </div>
              <div v-if="h.city" class="text-muted-foreground">{{ h.city }}</div>
            </CardContent>
          </Card>

          <Card>
            <CardHeader><CardTitle class="text-base">Health &amp; medical</CardTitle></CardHeader>
            <CardContent class="flex gap-3 text-sm text-muted-foreground">
              <Lock class="mt-0.5 size-4 shrink-0" />
              <p>
                Health details are hidden from customer service. Authorized medical staff see them on the camper record.
              </p>
            </CardContent>
          </Card>

          <Card>
            <CardHeader>
              <CardTitle class="text-base">Verification checklist</CardTitle>
              <CardDescription>{{ verified }} of {{ h.verification.length }} complete</CardDescription>
            </CardHeader>
            <CardContent class="space-y-3">
              <div v-for="v in h.verification" :key="v.key" class="flex gap-3">
                <Checkbox
                  :id="`verify-${v.key}`"
                  class="mt-0.5"
                  :model-value="v.checked"
                  :disabled="!canVerify"
                  @update:model-value="(c) => toggle(v.key, c === true)"
                />
                <div class="min-w-0">
                  <Label :for="`verify-${v.key}`" class="font-normal leading-snug">{{ v.label }}</Label>
                  <p v-if="v.checked" class="text-xs text-muted-foreground">
                    {{ v.checkedBy }} · {{ dateTime(v.checkedAt!) }}
                  </p>
                </div>
              </div>
              <p v-if="!canVerify" class="text-xs text-muted-foreground">
                Customer Experience staff complete this checklist.
              </p>
            </CardContent>
          </Card>

          <Card v-if="h.duplicates.length" class="border-amber-300">
            <CardHeader
              ><CardTitle class="flex items-center gap-2 text-base"
                ><AlertTriangle class="size-4 text-amber-600" />Possible duplicate</CardTitle
              ></CardHeader
            >
            <CardContent class="space-y-2 text-sm">
              <div v-for="d in h.duplicates" :key="d.otherHouseholdId" class="space-y-1">
                <p>{{ d.reason }} as household #{{ d.otherHouseholdId }}.</p>
                <Button size="sm" variant="outline" as-child>
                  <RouterLink :to="`/admin/duplicates/${d.householdA}/${d.householdB}`">Review and merge</RouterLink>
                </Button>
              </div>
            </CardContent>
          </Card>

          <Card>
            <CardHeader>
              <CardTitle class="text-base">Internal notes</CardTitle>
              <CardDescription>Staff only. Families never see these.</CardDescription>
            </CardHeader>
            <CardContent class="space-y-4">
              <form class="space-y-2" @submit.prevent="addNote">
                <Label for="note" class="sr-only">New note</Label>
                <Textarea
                  id="note"
                  v-model="note"
                  rows="3"
                  maxlength="2000"
                  placeholder="What did the family ask, and what did you tell them?"
                />
                <p v-if="noteError" class="text-sm text-destructive" role="alert">{{ noteError }}</p>
                <Button type="submit" size="sm" :disabled="savingNote">{{
                  savingNote ? 'Saving…' : 'Add note'
                }}</Button>
              </form>
              <ul class="space-y-3">
                <li v-for="n in h.notes" :key="n.id" class="rounded-md border bg-muted/30 p-3">
                  <p class="text-sm whitespace-pre-line">{{ n.body }}</p>
                  <p class="mt-1 text-xs text-muted-foreground">{{ n.author }} · {{ dateTime(n.createdAt) }}</p>
                </li>
              </ul>
              <p v-if="!h.notes.length" class="text-sm text-muted-foreground">No notes yet.</p>
            </CardContent>
          </Card>

          <p v-if="h.mergedFrom.length" class="text-xs text-muted-foreground">
            <template v-for="m in h.mergedFrom" :key="m.mergedHouseholdId">
              Household #{{ m.mergedHouseholdId }} merged in by {{ m.actor }} on {{ date(m.createdAt) }}.
            </template>
          </p>
          <p v-if="h.salesforce.id" class="flex items-center gap-1 text-xs text-muted-foreground">
            <ExternalLink class="size-3" /> Salesforce record {{ h.salesforce.id }}
          </p>
        </aside>
      </div>
    </template>
  </div>
</template>

<script setup lang="ts">
import type { ColumnDef } from '@tanstack/vue-table'
import {
  ArrowLeft,
  ChevronLeft,
  ChevronRight,
  Info,
  Loader2,
  MailWarning,
  MoreHorizontal,
  Search,
  Send,
  TriangleAlert,
  UserMinus,
} from '@lucide/vue'
import { computed, onMounted, ref, watch } from 'vue'
import { RouterLink } from 'vue-router'
import { toast } from 'vue-sonner'
import StatusBadge from '@/components/StatusBadge.vue'
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
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Checkbox } from '@/components/ui/checkbox'
import { DataTable } from '@/components/ui/data-table'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger } from '@/components/ui/dropdown-menu'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Skeleton } from '@/components/ui/skeleton'
import { api, ApiError } from '@/lib/api'
import { dateRange, dateTime, money } from '@/lib/format'
import type { Attendee, GroupDetail, ResendResult, SentLink } from './types'

const props = defineProps<{ id: number }>()
const group = ref<GroupDetail | null>(null)
const loadError = ref<string | null>(null)

async function load() {
  try {
    group.value = await api.get<GroupDetail>(`/groups/${props.id}`)
  } catch (e) {
    loadError.value =
      e instanceof ApiError && e.status === 404
        ? 'We couldn’t find this group. It may belong to another account.'
        : 'We couldn’t load your group. Check your connection and refresh.'
  }
}
onMounted(load)

const active = computed(() => group.value?.attendees.filter((a) => a.isActive) ?? [])
const withdrawn = computed(() => group.value?.attendees.filter((a) => !a.isActive) ?? [])
const requests = computed(() => active.value.filter((a) => a.withdrawal === 'Requested'))

// ── Search, filter, paging ───────────────────────────────────────────────
type Filter = 'all' | 'Complete' | 'Incomplete' | 'noEmail' | 'withdrawal'
const query = ref('')
const filter = ref<Filter>('all')
const page = ref(0)
const pageSize = 10
const filtered = computed(() => {
  const q = query.value.trim().toLowerCase()
  return active.value.filter((a) => {
    if (q && !a.name.toLowerCase().includes(q) && !(a.email ?? '').toLowerCase().includes(q)) return false
    if (filter.value === 'Complete' || filter.value === 'Incomplete') return a.formStatus === filter.value
    if (filter.value === 'noEmail') return !a.email
    if (filter.value === 'withdrawal') return a.withdrawal === 'Requested'
    return true
  })
})
const pages = computed(() => Math.max(1, Math.ceil(filtered.value.length / pageSize)))
const visible = computed(() => filtered.value.slice(page.value * pageSize, (page.value + 1) * pageSize))
watch([query, filter], () => (page.value = 0))
watch(pages, (n) => {
  if (page.value >= n) page.value = n - 1
})

// ── Selection and resend ─────────────────────────────────────────────────
const selected = ref<Set<number>>(new Set())
function toggle(id: number, on: boolean | 'indeterminate') {
  const next = new Set(selected.value)
  if (on === true) next.add(id)
  else next.delete(id)
  selected.value = next
}
const selectable = computed(() => visible.value.filter((a) => a.email && a.formStatus !== 'Complete'))
const allSelected = computed(
  () => selectable.value.length > 0 && selectable.value.every((a) => selected.value.has(a.id)),
)
function toggleAll(on: boolean | 'indeterminate') {
  const next = new Set(selected.value)
  for (const a of selectable.value) {
    if (on === true) next.add(a.id)
    else next.delete(a.id)
  }
  selected.value = next
}

const sending = ref(false)
const sentLinks = ref<SentLink[]>([])
const skipped = ref<string[]>([])
const linksOpen = ref(false)

async function resend(ids: number[]) {
  if (sending.value || !ids.length) return
  sending.value = true
  try {
    const res = await api.post<ResendResult>(`/groups/${props.id}/resend`, { attendeeIds: ids })
    sentLinks.value = res.sent
    skipped.value = res.skipped
    linksOpen.value = true
    selected.value = new Set()
    toast.success(`Sent ${res.sent.length} new ${res.sent.length === 1 ? 'link' : 'links'}. Older links stop working.`)
    await load()
  } catch (e) {
    toast.error(e instanceof ApiError ? e.message : 'We couldn’t send the links. Try again.')
  } finally {
    sending.value = false
  }
}
function fullLink(path: string) {
  return new URL(path, window.location.origin).toString()
}

// ── Add or update an email ───────────────────────────────────────────────
const emailFor = ref<Attendee | null>(null)
const emailValue = ref('')
const emailError = ref<string | null>(null)
const savingEmail = ref(false)
function openEmail(a: Attendee) {
  emailFor.value = a
  emailValue.value = a.email ?? ''
  emailError.value = null
}
async function saveEmail() {
  const a = emailFor.value
  if (!a || savingEmail.value) return
  savingEmail.value = true
  emailError.value = null
  try {
    const res = await api.put<{ id: number; email: string; link: SentLink | null }>(
      `/groups/${props.id}/attendees/${a.id}/email`,
      { email: emailValue.value },
    )
    emailFor.value = null
    if (res.link) {
      sentLinks.value = [res.link]
      skipped.value = []
      linksOpen.value = true
    }
    toast.success(res.link ? `Saved. We sent ${a.name} a secure link.` : `Saved ${a.name}'s email.`)
    await load()
  } catch (e) {
    emailError.value = e instanceof ApiError ? (e.errors.email?.[0] ?? e.message) : 'We couldn’t save the email.'
  } finally {
    savingEmail.value = false
  }
}

// ── Withdrawal requests ──────────────────────────────────────────────────
const approving = ref<Attendee | null>(null)
const busy = ref(false)
async function approve() {
  const a = approving.value
  if (!a || busy.value) return
  busy.value = true
  try {
    await api.post(`/groups/${props.id}/attendees/${a.id}/withdrawal/approve`)
    toast.success(`${a.name} withdrawn. ${money(share.value)} refunded and the seat released.`)
    approving.value = null
    await load()
  } catch (e) {
    toast.error(e instanceof ApiError ? e.message : 'We couldn’t approve the refund. Try again.')
  } finally {
    busy.value = false
  }
}
async function keep(a: Attendee) {
  if (busy.value) return
  busy.value = true
  try {
    await api.post(`/groups/${props.id}/attendees/${a.id}/withdrawal/decline`)
    toast.success(`${a.name} stays on the roster. We'll let them know.`)
    await load()
  } catch (e) {
    toast.error(e instanceof ApiError ? e.message : 'We couldn’t update the request. Try again.')
  } finally {
    busy.value = false
  }
}
const share = computed(() => group.value?.pricePerAttendeeCents ?? 0)

const columns: ColumnDef<Attendee>[] = [
  { id: 'select', header: '', meta: { class: 'w-10' } },
  { accessorKey: 'name', header: 'Attendee', meta: { cellClass: 'font-medium' } },
  { accessorKey: 'email', header: 'Email', meta: { cellClass: 'text-muted-foreground' } },
  { accessorKey: 'formStatus', header: 'Forms' },
  {
    accessorKey: 'linkSentAt',
    header: 'Link sent',
    cell: ({ row }) => (row.original.linkSentAt ? dateTime(row.original.linkSentAt) : '—'),
    meta: { class: 'hidden xl:table-cell', cellClass: 'whitespace-nowrap text-muted-foreground' },
  },
  { id: 'actions', header: '', meta: { class: 'w-44 text-right' } },
]
</script>

<template>
  <div class="mx-auto max-w-6xl px-4 py-6 md:py-10">
    <div v-if="loadError" class="max-w-xl">
      <Alert variant="destructive">
        <TriangleAlert class="size-4" />
        <AlertTitle>Can’t open this group</AlertTitle>
        <AlertDescription>{{ loadError }}</AlertDescription>
      </Alert>
      <Button as-child variant="outline" class="mt-4"><RouterLink to="/groups">Your groups</RouterLink></Button>
    </div>

    <div v-else-if="!group" class="space-y-4">
      <Skeleton class="h-10 w-72" />
      <div class="grid grid-cols-3 gap-3"><Skeleton v-for="i in 3" :key="i" class="h-24 rounded-xl" /></div>
      <Skeleton class="h-96 rounded-xl" />
    </div>

    <template v-else>
      <Button variant="ghost" size="sm" as-child class="-ml-2.5 mb-4 text-muted-foreground">
        <RouterLink to="/groups"><ArrowLeft />Your groups</RouterLink>
      </Button>
      <div class="flex flex-wrap items-end justify-between gap-4">
        <div>
          <p class="text-xs font-medium tracking-wide text-muted-foreground uppercase">
            {{ group.program.name }} · {{ dateRange(group.session.startDate, group.session.endDate) }}
          </p>
          <h1 class="mt-1 text-2xl font-semibold tracking-tight md:text-3xl">Attendee completion</h1>
          <p class="mt-1 flex flex-wrap items-center gap-2 text-muted-foreground">
            {{ group.name }} <StatusBadge :status="group.status" />
          </p>
        </div>
        <Button
          v-if="group.status === 'Confirmed'"
          :disabled="sending || !selected.size"
          @click="resend([...selected])"
        >
          <Loader2 v-if="sending" class="size-4 animate-spin" /><Send v-else class="size-4" /> Resend to selected{{
            selected.size ? ` (${selected.size})` : ''
          }}
        </Button>
      </div>

      <Alert v-if="group.status === 'Draft'" class="mt-6">
        <Info class="size-4" />
        <AlertTitle>This group isn’t paid for yet</AlertTitle>
        <AlertDescription>
          <span
            >Links go out once you pay.
            <RouterLink class="font-medium underline underline-offset-4" :to="`/groups/${group.id}/roster`"
              >Finish registering</RouterLink
            >.</span
          >
        </AlertDescription>
      </Alert>

      <Alert v-for="a in requests" :key="a.id" class="mt-6 border-amber-300 bg-amber-50/60">
        <UserMinus class="size-4" />
        <AlertTitle>{{ a.name }} asked to withdraw</AlertTitle>
        <AlertDescription>
          <p>
            <template v-if="a.withdrawalReason">“{{ a.withdrawalReason }}” · </template>Requested
            {{ a.withdrawalRequestedAt ? dateTime(a.withdrawalRequestedAt) : '' }}. Approving refunds
            {{ money(share) }} to the card ending {{ group.payment?.cardLast4 ?? '····' }} and frees their seat.
          </p>
          <div class="mt-3 flex flex-wrap gap-2">
            <Button size="sm" :disabled="busy" @click="approving = a">Approve refund</Button>
            <Button size="sm" variant="outline" :disabled="busy" @click="keep(a)">Keep attendee</Button>
          </div>
        </AlertDescription>
      </Alert>

      <div class="mt-6 grid grid-cols-3 gap-3">
        <Card class="gap-1 py-4">
          <CardHeader class="px-4"><CardDescription>Attendees</CardDescription></CardHeader>
          <CardContent class="px-4 text-2xl font-semibold tabular-nums" data-testid="kpi-attendees">{{
            group.counts.attendees
          }}</CardContent>
        </Card>
        <Card class="gap-1 py-4">
          <CardHeader class="px-4"><CardDescription>Complete</CardDescription></CardHeader>
          <CardContent class="px-4 text-2xl font-semibold text-emerald-700 tabular-nums" data-testid="kpi-complete">{{
            group.counts.complete
          }}</CardContent>
        </Card>
        <Card class="gap-1 py-4">
          <CardHeader class="px-4"><CardDescription>Incomplete</CardDescription></CardHeader>
          <CardContent class="px-4 text-2xl font-semibold text-amber-700 tabular-nums" data-testid="kpi-incomplete">{{
            group.counts.incomplete
          }}</CardContent>
        </Card>
      </div>

      <p class="mt-4 flex items-start gap-2 text-sm text-muted-foreground">
        <Info class="mt-0.5 size-4 shrink-0" />Attendees complete their own forms. You can't complete forms on their
        behalf; resend a link if someone can't find theirs.
      </p>

      <div class="mt-6 flex flex-col gap-3 sm:flex-row sm:items-center">
        <div class="relative sm:max-w-xs sm:flex-1">
          <Search class="pointer-events-none absolute top-1/2 left-2.5 size-4 -translate-y-1/2 text-muted-foreground" />
          <Input v-model="query" class="pl-8" placeholder="Search by name or email" aria-label="Search attendees" />
        </div>
        <Select v-model="filter">
          <SelectTrigger class="sm:w-52" aria-label="Filter by status"><SelectValue /></SelectTrigger>
          <SelectContent>
            <SelectItem value="all">All attendees</SelectItem>
            <SelectItem value="Complete">Complete</SelectItem>
            <SelectItem value="Incomplete">Incomplete</SelectItem>
            <SelectItem value="noEmail">No email</SelectItem>
            <SelectItem value="withdrawal">Withdrawal requested</SelectItem>
          </SelectContent>
        </Select>
        <label
          v-if="group.status === 'Confirmed' && selectable.length"
          class="flex items-center gap-2 text-sm sm:ml-auto"
        >
          <Checkbox :model-value="allSelected" @update:model-value="toggleAll" />Select all incomplete
        </label>
      </div>

      <!-- Desktop table -->
      <DataTable
        class="mt-4 hidden md:block"
        :columns="columns"
        :data="visible"
        :get-row-id="(a) => String(a.id)"
        empty-text="No attendees match."
      >
        <template #cell-select="{ row }">
          <Checkbox
            v-if="group.status === 'Confirmed' && row.email && row.formStatus !== 'Complete'"
            :model-value="selected.has(row.id)"
            :aria-label="`Select ${row.name}`"
            @update:model-value="(v) => toggle(row.id, v)"
          />
        </template>
        <template #cell-name="{ row }">
          <span class="flex flex-wrap items-center gap-2">
            {{ row.name }}
            <span v-if="row.withdrawal === 'Requested'" class="text-xs font-normal text-amber-700"
              >Withdrawal requested</span
            >
          </span>
        </template>
        <template #cell-email="{ row }">
          <span v-if="row.email">{{ row.email }}</span>
          <span v-else class="flex items-center gap-1 text-amber-700"><MailWarning class="size-3.5" />No email</span>
        </template>
        <template #cell-formStatus="{ row }"><StatusBadge :status="row.formStatus" /></template>
        <template #cell-actions="{ row }">
          <div v-if="group.status === 'Confirmed'" class="flex items-center justify-end gap-1">
            <Button
              v-if="row.email && row.formStatus !== 'Complete'"
              variant="outline"
              size="sm"
              :disabled="sending"
              @click="resend([row.id])"
              >Resend link</Button
            >
            <DropdownMenu>
              <DropdownMenuTrigger as-child>
                <Button variant="ghost" size="icon" :aria-label="`Actions for ${row.name}`"><MoreHorizontal /></Button>
              </DropdownMenuTrigger>
              <DropdownMenuContent align="end">
                <DropdownMenuItem :disabled="!row.email || sending" @select="resend([row.id])"
                  >Resend link</DropdownMenuItem
                >
                <DropdownMenuItem @select="openEmail(row)">{{
                  row.email ? 'Update email' : 'Add email'
                }}</DropdownMenuItem>
              </DropdownMenuContent>
            </DropdownMenu>
          </div>
        </template>
      </DataTable>

      <!-- Phone list -->
      <ul class="mt-4 divide-y rounded-lg border md:hidden">
        <li v-if="!visible.length" class="p-6 text-center text-sm text-muted-foreground">No attendees match.</li>
        <li v-for="a in visible" :key="a.id" class="flex items-start gap-3 p-3">
          <Checkbox
            v-if="group.status === 'Confirmed' && a.email && a.formStatus !== 'Complete'"
            class="mt-1"
            :model-value="selected.has(a.id)"
            :aria-label="`Select ${a.name}`"
            @update:model-value="(v) => toggle(a.id, v)"
          />
          <span v-else class="w-4 shrink-0" />
          <div class="min-w-0 flex-1">
            <p class="flex flex-wrap items-center gap-2 font-medium">
              {{ a.name }} <StatusBadge :status="a.formStatus" />
            </p>
            <p v-if="a.email" class="truncate text-sm text-muted-foreground">{{ a.email }}</p>
            <p v-else class="flex items-center gap-1 text-sm text-amber-700">
              <MailWarning class="size-3.5" />No email
            </p>
            <p v-if="a.withdrawal === 'Requested'" class="text-xs text-amber-700">Withdrawal requested</p>
          </div>
          <DropdownMenu v-if="group.status === 'Confirmed'">
            <DropdownMenuTrigger as-child>
              <Button variant="ghost" size="icon" :aria-label="`Actions for ${a.name}`"><MoreHorizontal /></Button>
            </DropdownMenuTrigger>
            <DropdownMenuContent align="end">
              <DropdownMenuItem :disabled="!a.email || sending" @select="resend([a.id])">Resend link</DropdownMenuItem>
              <DropdownMenuItem @select="openEmail(a)">{{ a.email ? 'Update email' : 'Add email' }}</DropdownMenuItem>
            </DropdownMenuContent>
          </DropdownMenu>
        </li>
      </ul>

      <div class="mt-3 flex items-center justify-between text-sm text-muted-foreground">
        <span
          >Showing {{ visible.length ? page * pageSize + 1 : 0 }}–{{ page * pageSize + visible.length }} of
          {{ filtered.length }}</span
        >
        <div v-if="pages > 1" class="flex items-center gap-1">
          <Button variant="outline" size="icon" :disabled="page === 0" aria-label="Previous page" @click="page--"
            ><ChevronLeft
          /></Button>
          <span class="px-2 tabular-nums">{{ page + 1 }} / {{ pages }}</span>
          <Button variant="outline" size="icon" :disabled="page >= pages - 1" aria-label="Next page" @click="page++"
            ><ChevronRight
          /></Button>
        </div>
      </div>

      <div class="mt-8 grid gap-4 md:grid-cols-2">
        <Card v-if="group.payment">
          <CardHeader>
            <CardTitle class="text-base">Payment</CardTitle>
            <CardDescription>Confirmation {{ group.payment.confirmationCode }}</CardDescription>
          </CardHeader>
          <CardContent class="space-y-2 text-sm">
            <div class="flex justify-between">
              <span>Charged</span><span class="tabular-nums">{{ money(group.payment.chargedCents) }}</span>
            </div>
            <div class="flex justify-between">
              <span>Per attendee</span
              ><span class="tabular-nums">{{ money(group.payment.pricePerAttendeeCents) }}</span>
            </div>
            <div v-if="group.payment.refundedCents" class="flex justify-between">
              <span>Refunded</span><span class="tabular-nums">−{{ money(group.payment.refundedCents) }}</span>
            </div>
            <p class="text-muted-foreground">
              Card ending {{ group.payment.cardLast4
              }}<template v-if="group.payment.paidAt"> · paid {{ dateTime(group.payment.paidAt) }}</template>
            </p>
          </CardContent>
        </Card>
        <Card v-if="withdrawn.length">
          <CardHeader>
            <CardTitle class="text-base">Withdrawn</CardTitle>
            <CardDescription>Refunded and removed from the roster.</CardDescription>
          </CardHeader>
          <CardContent>
            <ul class="space-y-2 text-sm">
              <li v-for="a in withdrawn" :key="a.id" class="flex items-center justify-between gap-2">
                <span>{{ a.name }}</span
                ><StatusBadge status="Cancelled" />
              </li>
            </ul>
          </CardContent>
        </Card>
      </div>
    </template>

    <Dialog v-model:open="linksOpen">
      <DialogContent class="sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>{{ sentLinks.length === 1 ? 'Link sent' : `${sentLinks.length} links sent` }}</DialogTitle>
          <DialogDescription>
            Each attendee gets their link by email. This prototype doesn't send email, so the links are shown here for
            the demo only.
          </DialogDescription>
        </DialogHeader>
        <ul class="max-h-80 space-y-3 overflow-y-auto">
          <li v-for="l in sentLinks" :key="l.attendeeId" class="rounded-md border p-3 text-sm">
            <p class="font-medium">{{ l.name }}</p>
            <p class="text-muted-foreground">{{ l.email }}</p>
            <div class="mt-2 flex items-center gap-2">
              <code class="min-w-0 flex-1 truncate rounded bg-muted px-2 py-1 text-xs">{{ fullLink(l.link) }}</code>
              <Button size="sm" variant="outline" as-child>
                <a :href="fullLink(l.link)" target="_blank" rel="noopener">Open link</a>
              </Button>
            </div>
          </li>
        </ul>
        <p v-if="skipped.length" class="text-sm text-muted-foreground">Not sent: {{ skipped.join('; ') }}</p>
        <DialogFooter><Button @click="linksOpen = false">Done</Button></DialogFooter>
      </DialogContent>
    </Dialog>

    <Dialog :open="!!emailFor" @update:open="(o) => !o && (emailFor = null)">
      <DialogContent class="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>{{ emailFor?.email ? 'Update email' : 'Add email' }}</DialogTitle>
          <DialogDescription>We'll send {{ emailFor?.name }} a new secure link at this address.</DialogDescription>
        </DialogHeader>
        <form class="space-y-2" @submit.prevent="saveEmail">
          <Label for="attendee-email">Email</Label>
          <Input id="attendee-email" v-model="emailValue" type="email" :aria-invalid="!!emailError" />
          <p v-if="emailError" class="text-sm text-destructive">{{ emailError }}</p>
        </form>
        <DialogFooter>
          <Button variant="outline" @click="emailFor = null">Cancel</Button>
          <Button :disabled="savingEmail || !emailValue.trim()" @click="saveEmail">
            <Loader2 v-if="savingEmail" class="size-4 animate-spin" />Save and send link
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>

    <AlertDialog :open="!!approving" @update:open="(o) => !o && (approving = null)">
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>Withdraw {{ approving?.name }} and refund {{ money(share) }}?</AlertDialogTitle>
          <AlertDialogDescription>
            The refund goes to the card ending {{ group?.payment?.cardLast4 }}, their seat is released, and their link
            stops working. This can't be undone.
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel :disabled="busy">Cancel</AlertDialogCancel>
          <Button :disabled="busy" @click="approve">
            <Loader2 v-if="busy" class="size-4 animate-spin" />Approve refund
          </Button>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  </div>
</template>

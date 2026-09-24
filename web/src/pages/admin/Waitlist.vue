<script setup lang="ts">
import type { ColumnDef } from '@tanstack/vue-table'
import { onMounted, ref } from 'vue'
import { RouterLink } from 'vue-router'
import { toast } from 'vue-sonner'
import StatusBadge from '@/components/StatusBadge.vue'
import { useAdminScope } from '@/composables/useAdminScope'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { DataTable } from '@/components/ui/data-table'
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
import { Skeleton } from '@/components/ui/skeleton'
import { api, ApiError } from '@/lib/api'
import { dateTime } from '@/lib/format'

interface Entry {
  id: number
  position: number
  participant: string
  grade: number
  guardian: string
  status: string
  offerExpiresAt: string | null
  createdAt: string
}
interface PoolWaitlist {
  id: number
  name: string
  capacity: number
  reserved: number
  remaining: number
  entries: Entry[]
}

const columns: ColumnDef<Entry>[] = [
  { accessorKey: 'position', header: '#', meta: { class: 'w-12', cellClass: 'tabular-nums' } },
  { accessorKey: 'participant', header: 'Camper', meta: { cellClass: 'font-medium' } },
  { accessorKey: 'grade', header: 'Grade', meta: { cellClass: 'tabular-nums' } },
  {
    accessorKey: 'guardian',
    header: 'Guardian',
    meta: { class: 'hidden lg:table-cell', cellClass: 'text-muted-foreground' },
  },
  {
    accessorKey: 'createdAt',
    header: 'Joined',
    cell: ({ row }) => dateTime(row.original.createdAt),
    meta: { class: 'hidden xl:table-cell', cellClass: 'text-muted-foreground' },
  },
  { accessorKey: 'status', header: 'Status' },
  {
    accessorKey: 'offerExpiresAt',
    header: 'Offer expires',
    cell: ({ row }) =>
      row.original.status === 'Offered' && row.original.offerExpiresAt ? dateTime(row.original.offerExpiresAt) : '—',
    meta: { cellClass: 'whitespace-nowrap text-muted-foreground' },
  },
  { id: 'actions', header: 'Actions', meta: { class: 'text-right', cellClass: 'space-x-2 whitespace-nowrap' } },
]

const { sessionId, ready } = useAdminScope()
const pools = ref<PoolWaitlist[] | null>(null)
async function load() {
  pools.value = (await api.get<{ pools: PoolWaitlist[] }>(`/admin/sessions/${sessionId.value}/waitlist`)).pools
}
onMounted(async () => {
  await ready
  if (sessionId.value) await load()
})

// ── Dialog: offer or remove ──
const target = ref<{ mode: 'offer' | 'remove'; entry: Entry; pool: PoolWaitlist } | null>(null)
const deadline = ref('')
const reason = ref('')
const busy = ref(false)
const error = ref<string | null>(null)

const pad = (n: number) => String(n).padStart(2, '0')
function localInput(d: Date) {
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`
}
function openOffer(entry: Entry, pool: PoolWaitlist) {
  target.value = { mode: 'offer', entry, pool }
  deadline.value = localInput(new Date(Date.now() + 48 * 3600_000))
  error.value = null
}
function openRemove(entry: Entry, pool: PoolWaitlist) {
  target.value = { mode: 'remove', entry, pool }
  reason.value = ''
  error.value = null
}
async function submit() {
  if (!target.value) return
  busy.value = true
  error.value = null
  const { mode, entry } = target.value
  try {
    if (mode === 'offer') {
      await api.post(`/admin/waitlist/${entry.id}/offer`, { deadline: new Date(deadline.value).toISOString() })
      toast.success(`Spot offered to ${entry.participant}. The family has been emailed.`)
    } else {
      if (!reason.value.trim()) {
        error.value = 'Add a reason.'
        return
      }
      await api.post(`/admin/waitlist/${entry.id}/remove`, { reason: reason.value })
      toast.success(`${entry.participant} removed from the waitlist.`)
    }
    target.value = null
    await load()
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : "That didn't go through. Check your connection and try again."
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <div class="space-y-6">
    <Skeleton v-if="!pools" class="h-64 rounded-xl" />
    <Card v-else-if="!pools.length">
      <CardContent class="py-10 text-center text-muted-foreground">No waitlists for this session.</CardContent>
    </Card>
    <Card v-for="p in pools" :key="p.id">
      <CardHeader>
        <CardTitle>{{ p.name }}</CardTitle>
        <CardDescription>
          {{ p.reserved }} / {{ p.capacity }} taken ·
          <span :class="p.remaining > 0 ? 'font-medium text-emerald-700' : ''">{{
            p.remaining > 0 ? `${p.remaining} open to offer` : 'Full'
          }}</span>
          <template v-if="p.remaining === 0">
            ·
            <Button variant="link" as-child class="h-auto p-0 text-sm"
              ><RouterLink to="/admin/session">raise capacity</RouterLink></Button
            >
            or wait for a cancellation</template
          >
        </CardDescription>
      </CardHeader>
      <CardContent>
        <DataTable :columns="columns" :data="p.entries" :get-row-id="(e) => String(e.id)" empty-text="Nobody waiting.">
          <template #cell-status="{ row: e }"><StatusBadge :status="e.status" /></template>
          <template #cell-actions="{ row: e }">
            <Button
              v-if="e.status === 'Waiting'"
              size="sm"
              :disabled="p.remaining <= 0"
              :title="p.remaining <= 0 ? 'No open spots in this pool' : undefined"
              @click="openOffer(e, p)"
              >Offer spot</Button
            >
            <Button
              v-if="e.status === 'Waiting' || e.status === 'Offered'"
              size="sm"
              variant="ghost"
              @click="openRemove(e, p)"
              >Remove</Button
            >
          </template>
        </DataTable>
      </CardContent>
    </Card>

    <Dialog
      :open="!!target"
      @update:open="
        (v) => {
          if (!v) target = null
        }
      "
    >
      <DialogContent v-if="target">
        <DialogHeader>
          <DialogTitle>{{
            target.mode === 'offer'
              ? `Offer ${target.pool.name} spot to ${target.entry.participant}?`
              : `Remove ${target.entry.participant}?`
          }}</DialogTitle>
          <DialogDescription>
            <template v-if="target.mode === 'offer'"
              >This holds one seat until the deadline. The family gets an email with a link to accept and pay. If they
              don't respond, the seat is released automatically.</template
            >
            <template v-else
              >They'll be taken off the {{ target.pool.name }} waitlist.<template
                v-if="target.entry.status === 'Offered'"
              >
                The held seat is released.</template
              ></template
            >
          </DialogDescription>
        </DialogHeader>
        <div v-if="target.mode === 'offer'" class="space-y-2">
          <Label for="deadline">Respond by</Label>
          <Input id="deadline" v-model="deadline" type="datetime-local" />
        </div>
        <div v-else class="space-y-2">
          <Label for="reason">Reason</Label>
          <Input id="reason" v-model="reason" placeholder="e.g. Family asked to be removed" />
        </div>
        <p v-if="error" class="text-sm text-destructive" role="alert">{{ error }}</p>
        <DialogFooter>
          <Button variant="outline" :disabled="busy" @click="target = null">Cancel</Button>
          <Button :variant="target.mode === 'remove' ? 'destructive' : 'default'" :disabled="busy" @click="submit">
            {{ busy ? 'Working…' : target.mode === 'offer' ? 'Send offer' : 'Remove' }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  </div>
</template>

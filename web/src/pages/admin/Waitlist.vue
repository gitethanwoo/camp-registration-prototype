<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { RouterLink } from 'vue-router'
import { toast } from 'vue-sonner'
import StatusBadge from '@/components/StatusBadge.vue'
import { useAdminScope } from '@/composables/useAdminScope'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Skeleton } from '@/components/ui/skeleton'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { api, ApiError } from '@/lib/api'
import { dateTime } from '@/lib/format'

interface Entry { id: number, position: number, participant: string, grade: number, guardian: string, status: string, offerExpiresAt: string | null, createdAt: string }
interface PoolWaitlist { id: number, name: string, capacity: number, reserved: number, remaining: number, entries: Entry[] }

const { sessionId, ready } = useAdminScope()
const pools = ref<PoolWaitlist[] | null>(null)
async function load() { pools.value = (await api.get<{ pools: PoolWaitlist[] }>(`/admin/sessions/${sessionId.value}/waitlist`)).pools }
onMounted(async () => { await ready; if (sessionId.value) await load() })

// ── Dialog: offer or remove ──
const target = ref<{ mode: 'offer' | 'remove', entry: Entry, pool: PoolWaitlist } | null>(null)
const deadline = ref('')
const reason = ref('')
const busy = ref(false)
const error = ref<string | null>(null)

function localInput(d: Date) {
  const pad = (n: number) => String(n).padStart(2, '0')
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
async function confirm() {
  if (!target.value) return
  busy.value = true
  error.value = null
  const { mode, entry } = target.value
  try {
    if (mode === 'offer') {
      await api.post(`/admin/waitlist/${entry.id}/offer`, { deadline: new Date(deadline.value).toISOString() })
      toast.success(`Spot offered to ${entry.participant}. The family has been emailed.`)
    }
    else {
      if (!reason.value.trim()) { error.value = 'Add a reason.'; return }
      await api.post(`/admin/waitlist/${entry.id}/remove`, { reason: reason.value })
      toast.success(`${entry.participant} removed from the waitlist.`)
    }
    target.value = null
    await load()
  }
  catch (e) { error.value = e instanceof ApiError ? e.message : 'Something went wrong.' }
  finally { busy.value = false }
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
          <span :class="p.remaining > 0 ? 'font-medium text-emerald-700' : ''">{{ p.remaining > 0 ? `${p.remaining} open to offer` : 'Full' }}</span>
          <template v-if="p.remaining === 0"> · <RouterLink to="/admin/session" class="underline">raise capacity</RouterLink> or wait for a cancellation</template>
        </CardDescription>
      </CardHeader>
      <CardContent>
        <div class="rounded-lg border">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead class="w-12">#</TableHead>
                <TableHead>Camper</TableHead>
                <TableHead class="hidden xl:table-cell">Joined</TableHead>
                <TableHead>Status</TableHead>
                <TableHead class="text-right">Actions</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              <TableRow v-if="!p.entries.length"><TableCell colspan="5" class="text-center text-muted-foreground">Nobody waiting.</TableCell></TableRow>
              <TableRow v-for="e in p.entries" :key="e.id">
                <TableCell class="tabular-nums">{{ e.position }}</TableCell>
                <TableCell>
                  <div class="font-medium">{{ e.participant }}</div>
                  <div class="text-xs text-muted-foreground">Grade {{ e.grade }} · {{ e.guardian }}</div>
                </TableCell>
                <TableCell class="hidden text-muted-foreground xl:table-cell">{{ dateTime(e.createdAt) }}</TableCell>
                <TableCell>
                  <StatusBadge :status="e.status" />
                  <div v-if="e.offerExpiresAt && e.status === 'Offered'" class="mt-1 text-xs text-muted-foreground">until {{ dateTime(e.offerExpiresAt) }}</div>
                </TableCell>
                <TableCell class="space-x-2 text-right whitespace-nowrap">
                  <Button v-if="e.status === 'Waiting'" size="sm" :disabled="p.remaining <= 0" :title="p.remaining <= 0 ? 'No open spots in this pool' : undefined" @click="openOffer(e, p)">Offer spot</Button>
                  <Button v-if="e.status === 'Waiting' || e.status === 'Offered'" size="sm" variant="ghost" @click="openRemove(e, p)">Remove</Button>
                </TableCell>
              </TableRow>
            </TableBody>
          </Table>
        </div>
      </CardContent>
    </Card>

    <Dialog :open="!!target" @update:open="v => { if (!v) target = null }">
      <DialogContent v-if="target">
        <DialogHeader>
          <DialogTitle>{{ target.mode === 'offer' ? `Offer ${target.pool.name} spot to ${target.entry.participant}?` : `Remove ${target.entry.participant}?` }}</DialogTitle>
          <DialogDescription>
            <template v-if="target.mode === 'offer'">This holds one seat until the deadline. The family gets an email with a link to accept and pay. If they don't respond, the seat is released automatically.</template>
            <template v-else>They'll be taken off the {{ target.pool.name }} waitlist.<template v-if="target.entry.status === 'Offered'"> The held seat is released.</template></template>
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
          <Button :variant="target.mode === 'remove' ? 'destructive' : 'default'" :disabled="busy" @click="confirm">
            {{ busy ? 'Working…' : target.mode === 'offer' ? 'Send offer' : 'Remove' }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  </div>
</template>

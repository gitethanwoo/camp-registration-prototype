<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { toast } from 'vue-sonner'
import { useAdminScope } from '@/composables/useAdminScope'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Skeleton } from '@/components/ui/skeleton'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { api, ApiError } from '@/lib/api'
import { date, dateRange, money } from '@/lib/format'

interface Pool { id: number, name: string, gender: string | null, gradeMin: number, gradeMax: number, capacity: number, reserved: number, remaining: number, waitlisted: number }
interface SessionDetail {
  id: number, name: string, startDate: string, endDate: string, priceCents: number, depositCents: number, planInstallments: number, balanceDueDate: string, waitlistMode: string
  program: { name: string, ministry: string, location: string, healthMechanism: string, type: string }
  pools: Pool[]
}

const { sessionId, ready } = useAdminScope()
const s = ref<SessionDetail | null>(null)
const edits = ref<Record<number, string>>({})
const errors = ref<Record<number, string>>({})
const saving = ref<number | null>(null)

async function load() {
  s.value = await api.get<SessionDetail>(`/admin/sessions/${sessionId.value}`)
  edits.value = Object.fromEntries(s.value.pools.map(p => [p.id, String(p.capacity)]))
}
onMounted(async () => { await ready; if (sessionId.value) await load() })

async function save(p: Pool) {
  saving.value = p.id
  delete errors.value[p.id]
  try {
    await api.put(`/admin/pools/${p.id}`, { capacity: Number(edits.value[p.id]) })
    toast.success(`${p.name} capacity set to ${edits.value[p.id]}.`)
    await load()
  }
  catch (e) { errors.value[p.id] = e instanceof ApiError ? e.message : 'Save failed.' }
  finally { saving.value = null }
}
const audience = (p: Pool) => `${p.gender ? (p.gender === 'Male' ? 'Boys' : 'Girls') + ', ' : ''}${p.gradeMin >= 99 ? 'adults' : p.gradeMin === p.gradeMax ? `grade ${p.gradeMin}` : `grades ${p.gradeMin}–${p.gradeMax}`}`
</script>

<template>
  <div class="space-y-6">
    <Skeleton v-if="!s" class="h-96 rounded-xl" />
    <template v-else>
      <Card>
        <CardHeader>
          <CardTitle>{{ s.program.name }} · {{ s.name }}</CardTitle>
          <CardDescription>{{ s.program.ministry }} · {{ s.program.location }}</CardDescription>
        </CardHeader>
        <CardContent>
          <dl class="grid gap-4 text-sm sm:grid-cols-2 lg:grid-cols-4">
            <div><dt class="text-muted-foreground">Dates</dt><dd>{{ dateRange(s.startDate, s.endDate) }}</dd></div>
            <div><dt class="text-muted-foreground">Price</dt><dd>{{ money(s.priceCents) }}<template v-if="s.depositCents"> · {{ money(s.depositCents) }} deposit</template></dd></div>
            <div><dt class="text-muted-foreground">Payment plan</dt><dd>{{ s.planInstallments ? `${s.planInstallments} monthly, final by ${date(s.balanceDueDate)}` : 'Not offered' }}</dd></div>
            <div><dt class="text-muted-foreground">Waitlist</dt><dd>{{ s.waitlistMode === 'AdminApproval' ? 'Staff offer spots in order' : s.waitlistMode }}</dd></div>
            <div><dt class="text-muted-foreground">Health forms</dt><dd>{{ s.program.healthMechanism === 'CampDoc' ? 'CampDoc' : 'Built-in form' }}</dd></div>
            <div><dt class="text-muted-foreground">Registration type</dt><dd>{{ s.program.type }}</dd></div>
          </dl>
          <p class="mt-4 text-xs text-muted-foreground">Session details are read-only in this prototype; capacity is editable below.</p>
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>Capacity pools</CardTitle>
          <CardDescription>Each camper is placed in exactly one pool by gender and grade. Capacity can't drop below seats already taken.</CardDescription>
        </CardHeader>
        <CardContent>
          <div class="rounded-lg border">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Pool</TableHead>
                  <TableHead>Who</TableHead>
                  <TableHead class="text-right">Taken</TableHead>
                  <TableHead class="text-right">Waitlist</TableHead>
                  <TableHead class="w-48">Capacity</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                <TableRow v-for="p in s.pools" :key="p.id">
                  <TableCell class="font-medium">{{ p.name }}</TableCell>
                  <TableCell class="text-muted-foreground">{{ audience(p) }}</TableCell>
                  <TableCell class="text-right tabular-nums">{{ p.reserved }}</TableCell>
                  <TableCell class="text-right tabular-nums">{{ p.waitlisted || '—' }}</TableCell>
                  <TableCell>
                    <form class="flex items-center gap-2" @submit.prevent="save(p)">
                      <Input v-model="edits[p.id]" type="number" :min="p.reserved" class="h-8 w-20" :aria-label="`${p.name} capacity`" :aria-invalid="!!errors[p.id] || undefined" />
                      <Button size="sm" variant="outline" type="submit" :disabled="saving === p.id || Number(edits[p.id]) === p.capacity">Save</Button>
                    </form>
                    <p v-if="errors[p.id]" class="mt-1 text-xs text-destructive" role="alert">{{ errors[p.id] }}</p>
                  </TableCell>
                </TableRow>
              </TableBody>
            </Table>
          </div>
        </CardContent>
      </Card>
    </template>
  </div>
</template>

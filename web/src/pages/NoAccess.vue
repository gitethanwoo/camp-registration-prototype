<script setup lang="ts">
import { computed } from 'vue'
import { RouterLink, useRoute } from 'vue-router'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardFooter, CardHeader, CardTitle } from '@/components/ui/card'
import { roleLabels, useSession } from '@/lib/session'

const route = useRoute()
const { session, roleLabel, signOut } = useSession()
const need = computed(() => String(route.query.need ?? ''))
// A staff page that needs a role the signed-in person doesn't have (router guard on meta.roles).
const neededRoles = computed(() =>
  String(route.query.roles ?? '')
    .split(',')
    .map((r) => roleLabels[r])
    .filter(Boolean),
)
const neededLabel = computed(() => {
  const r = neededRoles.value
  return r.length <= 1 ? (r[0] ?? 'another') : `${r.slice(0, -1).join(', ')} or ${r[r.length - 1]}`
})
</script>

<template>
  <div class="mx-auto max-w-md px-4 py-16">
    <Card v-if="need === 'role'">
      <CardHeader>
        <CardTitle>You don't have access to this page</CardTitle>
        <CardDescription>
          This page needs the {{ neededLabel }} role. You're signed in as {{ session?.name
          }}<template v-if="roleLabel"> ({{ roleLabel }})</template>. Ask an administrator if you need it.
        </CardDescription>
      </CardHeader>
      <CardContent />
      <CardFooter class="gap-2">
        <Button as-child><RouterLink to="/admin">Go to the staff console</RouterLink></Button>
        <Button variant="ghost" @click="signOut">Sign out</Button>
      </CardFooter>
    </Card>
    <Card v-else>
      <CardHeader>
        <CardTitle>{{ need === 'staff' ? 'Staff only' : 'This page is for families' }}</CardTitle>
        <CardDescription v-if="need === 'staff'">
          You're signed in as {{ session?.name }}, which isn't a WinShape staff account. Sign out and sign in with your
          staff account to open the console.
        </CardDescription>
        <CardDescription v-else>
          You're signed in as {{ session?.name }}, a staff account. Staff accounts don't have a household. Sign out and
          sign in as a family to register.
        </CardDescription>
      </CardHeader>
      <CardContent />
      <CardFooter class="gap-2">
        <Button @click="signOut">Sign out</Button>
        <Button variant="ghost" as-child>
          <RouterLink :to="need === 'staff' ? '/programs' : '/admin'">{{
            need === 'staff' ? 'Back to programs' : 'Go to the staff console'
          }}</RouterLink>
        </Button>
      </CardFooter>
    </Card>
  </div>
</template>

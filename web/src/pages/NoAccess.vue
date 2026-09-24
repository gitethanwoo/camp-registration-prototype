<script setup lang="ts">
import { computed } from 'vue'
import { RouterLink, useRoute } from 'vue-router'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardFooter, CardHeader, CardTitle } from '@/components/ui/card'
import { useSession } from '@/lib/session'

const route = useRoute()
const { session, signOut } = useSession()
const needsStaff = computed(() => route.query.need === 'staff')
</script>

<template>
  <div class="mx-auto max-w-md px-4 py-16">
    <Card>
      <CardHeader>
        <CardTitle>{{ needsStaff ? 'Staff only' : 'This page is for families' }}</CardTitle>
        <CardDescription v-if="needsStaff">
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
          <RouterLink :to="needsStaff ? '/programs' : '/admin'">{{
            needsStaff ? 'Back to programs' : 'Go to the staff console'
          }}</RouterLink>
        </Button>
      </CardFooter>
    </Card>
  </div>
</template>

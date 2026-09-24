<script setup lang="ts">
import { Lock } from '@lucide/vue'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'

// Shown when a setup page answers 403: the sidebar lists Setup for every staff role.
defineProps<{ page: string }>()
</script>

<template>
  <Alert class="mx-auto max-w-2xl">
    <Lock />
    <AlertTitle class="line-clamp-none">{{ page }} is for admins</AlertTitle>
    <AlertDescription>
      Only WinShape admins can change program setup. Ask an admin if something here needs to change. You can still read
      the audit log.
    </AlertDescription>
  </Alert>
</template>

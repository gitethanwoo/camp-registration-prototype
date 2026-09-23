<script setup lang="ts">
defineProps<{ compact?: boolean }>()
</script>

<template>
  <span class="flex items-center gap-2">
    <!-- WinShape red appears only in the logo. -->
    <svg viewBox="0 0 32 32" class="size-7 shrink-0" aria-hidden="true">
      <path d="M6 4c6-2 10 3 16 1l2 12c-6 2-10-3-16-1L10 28H7L6 4Z" fill="var(--brand)" />
    </svg>
    <span v-if="!compact" class="leading-none font-semibold tracking-tight">WinShape Camps</span>
  </span>
</template>

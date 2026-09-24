<script setup lang="ts">
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'

const faqs = [
  {
    q: 'Are spots held while I fill out forms?',
    a: 'No. We reserve spots the moment you pay. If a group fills while you’re registering, that child joins the waitlist automatically and isn’t charged.',
  },
  {
    q: 'What if my card is declined?',
    a: 'Nothing is charged, your spots are released, and you can try another card right away. Your answers and waivers are kept.',
  },
  {
    q: 'I clicked Pay twice. Was I charged twice?',
    a: 'No. Each checkout has a one-time key, so repeated clicks or retries return the original result.',
  },
  {
    q: 'How does the waitlist work?',
    a: 'Families are offered spots in order. You’ll get an email with a deadline to accept; if the deadline passes the spot goes to the next family.',
  },
  {
    q: 'Can I cancel?',
    a: 'Yes. Contact the camp office. Refunds follow the program’s cancellation policy, which depends on how close to camp you cancel.',
  },
]
</script>

<template>
  <div class="mx-auto max-w-3xl px-4 py-8 md:py-12">
    <h1 class="text-3xl font-semibold tracking-tight">Help</h1>
    <p class="mt-1 text-muted-foreground">Camp office: (706) 555-0142 · Mon–Fri 8:30–5 ET</p>
    <div class="mt-8 space-y-4">
      <Card v-for="f in faqs" :key="f.q">
        <CardHeader
          ><CardTitle class="text-base">{{ f.q }}</CardTitle></CardHeader
        >
        <CardContent class="text-sm text-muted-foreground">{{ f.a }}</CardContent>
      </Card>
    </div>
  </div>
</template>

<script setup lang="ts">
import { Lock } from '@lucide/vue'
import { computed } from 'vue'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { api } from '@/lib/api'

export interface CardFields {
  number: string
  expiry: string
  cvc: string
  zip: string
}

const card = defineModel<CardFields>({ required: true })
defineProps<{ disabled?: boolean }>()

const test = (number: string): CardFields => ({ number, expiry: '12 / 29', cvc: '123', zip: '30303' })

const complete = computed(
  () =>
    card.value.number.replace(/\D/g, '').length >= 15 &&
    /^\d{2}\s*\/\s*\d{2}$/.test(card.value.expiry) &&
    card.value.cvc.length >= 3 &&
    card.value.zip.length >= 5,
)

/** In production Fiserv's hosted iframe returns the token; the card number never reaches our API. */
async function tokenize() {
  return (await api.post<{ token: string }>('/fiserv-sandbox/tokenize', { cardNumber: card.value.number })).token
}

defineExpose({ complete, tokenize })
</script>

<template>
  <div>
    <!-- Stand-in for Fiserv hosted payment fields (an iframe in production). -->
    <fieldset class="space-y-4 rounded-lg border bg-muted/20 p-4" :disabled="disabled">
      <legend class="flex items-center gap-1.5 px-1 text-xs text-muted-foreground">
        <Lock class="size-3" />Secure payment by Fiserv · sandbox
      </legend>
      <div class="space-y-2">
        <Label for="pay-cc">Card number</Label>
        <Input
          id="pay-cc"
          v-model="card.number"
          inputmode="numeric"
          autocomplete="cc-number"
          placeholder="4242 4242 4242 4242"
        />
      </div>
      <div class="grid grid-cols-3 gap-3">
        <div class="space-y-2">
          <Label for="pay-exp">Expiry</Label>
          <Input id="pay-exp" v-model="card.expiry" autocomplete="cc-exp" placeholder="MM / YY" />
        </div>
        <div class="space-y-2">
          <Label for="pay-cvc">CVC</Label>
          <Input id="pay-cvc" v-model="card.cvc" inputmode="numeric" autocomplete="cc-csc" placeholder="123" />
        </div>
        <div class="space-y-2">
          <Label for="pay-zip">ZIP</Label>
          <Input id="pay-zip" v-model="card.zip" inputmode="numeric" autocomplete="postal-code" placeholder="30303" />
        </div>
      </div>
    </fieldset>
    <p class="mt-3 text-xs text-muted-foreground">
      Test cards:
      <Button
        type="button"
        variant="link"
        class="h-auto p-0 font-mono text-xs text-muted-foreground"
        :disabled="disabled"
        @click="card = test('4242 4242 4242 4242')"
        >4242 4242 4242 4242</Button
      >
      approves,
      <Button
        type="button"
        variant="link"
        class="h-auto p-0 font-mono text-xs text-muted-foreground"
        :disabled="disabled"
        @click="card = test('4000 0000 0000 0002')"
        >4000 0000 0000 0002</Button
      >
      declines.
    </p>
  </div>
</template>

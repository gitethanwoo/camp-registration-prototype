<script setup lang="ts">
import { Lock } from '@lucide/vue'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'

export interface CardInput {
  number: string
  expiry: string
  cvc: string
  zip: string
}

// Stand-in for Fiserv hosted payment fields (an iframe in production); the page only ever
// sends the token from /fiserv-sandbox/tokenize to our API.
const card = defineModel<CardInput>({ required: true })
defineProps<{ disabled?: boolean }>()

function fill(number: string) {
  card.value = { number, expiry: '12 / 29', cvc: '123', zip: '30303' }
}
</script>

<template>
  <fieldset class="space-y-4 rounded-lg border bg-muted/20 p-4" :disabled="disabled">
    <legend class="flex items-center gap-1.5 px-1 text-xs text-muted-foreground">
      <Lock class="size-3" />Secure payment by Fiserv · sandbox
    </legend>
    <div class="space-y-2">
      <Label for="g-cc">Card number</Label>
      <Input
        id="g-cc"
        v-model="card.number"
        inputmode="numeric"
        autocomplete="cc-number"
        placeholder="4242 4242 4242 4242"
      />
    </div>
    <div class="grid grid-cols-3 gap-3">
      <div class="space-y-2">
        <Label for="g-exp">Expiry</Label>
        <Input id="g-exp" v-model="card.expiry" autocomplete="cc-exp" placeholder="MM / YY" />
      </div>
      <div class="space-y-2">
        <Label for="g-cvc">CVC</Label>
        <Input id="g-cvc" v-model="card.cvc" inputmode="numeric" autocomplete="cc-csc" placeholder="123" />
      </div>
      <div class="space-y-2">
        <Label for="g-zip">ZIP</Label>
        <Input id="g-zip" v-model="card.zip" inputmode="numeric" autocomplete="postal-code" placeholder="30303" />
      </div>
    </div>
  </fieldset>
  <p class="mt-3 text-xs text-muted-foreground">
    Test cards:
    <Button
      variant="link"
      class="h-auto p-0 font-mono text-xs text-muted-foreground"
      @click="fill('4242 4242 4242 4242')"
      >4242 4242 4242 4242</Button
    >
    approves,
    <Button
      variant="link"
      class="h-auto p-0 font-mono text-xs text-muted-foreground"
      @click="fill('4000 0000 0000 0002')"
      >4000 0000 0000 0002</Button
    >
    declines.
  </p>
</template>

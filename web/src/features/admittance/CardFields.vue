<script setup lang="ts">
import { Lock } from '@lucide/vue'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import type { CardInput } from './card'

// Stand-in for Fiserv hosted payment fields (an iframe in production).
defineProps<{ disabled?: boolean }>()
const card = defineModel<CardInput>({ required: true })
</script>

<template>
  <div>
    <fieldset class="space-y-4 rounded-lg border bg-muted/20 p-4" :disabled="disabled">
      <legend class="flex items-center gap-1.5 px-1 text-xs text-muted-foreground">
        <Lock class="size-3" />Secure payment by Fiserv · sandbox
      </legend>
      <div class="space-y-2">
        <Label for="cc">Card number</Label>
        <Input
          id="cc"
          v-model="card.number"
          inputmode="numeric"
          autocomplete="cc-number"
          placeholder="4242 4242 4242 4242"
        />
      </div>
      <div class="grid grid-cols-3 gap-3">
        <div class="space-y-2">
          <Label for="exp">Expiry</Label>
          <Input id="exp" v-model="card.expiry" autocomplete="cc-exp" placeholder="MM / YY" />
        </div>
        <div class="space-y-2">
          <Label for="cvc">CVC</Label>
          <Input id="cvc" v-model="card.cvc" inputmode="numeric" autocomplete="cc-csc" placeholder="123" />
        </div>
        <div class="space-y-2">
          <Label for="zip">ZIP</Label>
          <Input id="zip" v-model="card.zip" inputmode="numeric" autocomplete="postal-code" placeholder="30303" />
        </div>
      </div>
    </fieldset>
    <p class="mt-3 text-xs text-muted-foreground">
      Test cards:
      <Button
        variant="link"
        class="h-auto p-0 font-mono text-xs text-muted-foreground"
        :disabled="disabled"
        @click="card = { number: '4242 4242 4242 4242', expiry: '12 / 29', cvc: '123', zip: '30303' }"
        >4242 4242 4242 4242</Button
      >
      approves ·
      <Button
        variant="link"
        class="h-auto p-0 font-mono text-xs text-muted-foreground"
        :disabled="disabled"
        @click="card = { number: '4000 0000 0000 0002', expiry: '12 / 29', cvc: '123', zip: '30303' }"
        >4000 0000 0000 0002</Button
      >
      declines
    </p>
  </div>
</template>

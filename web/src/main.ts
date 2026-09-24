import '@fontsource-variable/inter'
import { createApp } from 'vue'
import App from './App.vue'
import { loadClock } from './lib/clock'
import { router } from './router'
import './style.css'

// The demo clock's offset first, so the first render already shows the demo's "today".
void loadClock().then(() => createApp(App).use(router).mount('#app'))

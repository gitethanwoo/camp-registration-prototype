# Feature slices

Each slice owns `src/features/<slice>/`. To add pages without touching shared files, export route arrays from `routes.ts`:

```ts
import type { RouteRecordRaw } from 'vue-router'
import { Inbox } from '@lucide/vue'

// Children of the guest site ("/"). Paths are relative, e.g. 'family/children'.
export const routes: RouteRecordRaw[] = [
  {
    path: 'family/children/new',
    component: () => import('./AddChild.vue'),
    meta: { title: 'Add a child', auth: 'family' },
  },
]

// Children of the staff console ("/admin"). `meta.nav` adds a sidebar entry.
export const adminRoutes: RouteRecordRaw[] = [
  {
    path: 'discounts',
    component: () => import('./DiscountQueue.vue'),
    meta: {
      title: 'Discount approvals',
      nav: { group: 'Front desk', label: 'Discount approvals', icon: Inbox, order: 40 },
    },
  },
]
```

`src/router.ts` picks these up with `import.meta.glob`. Sidebar groups, in order: Front desk, Setup, Finance, Operations, Hosts. Guest routes that need a signed-in family set `meta.auth: 'family'`. Admin routes inherit `auth: 'staff'` from the layout.

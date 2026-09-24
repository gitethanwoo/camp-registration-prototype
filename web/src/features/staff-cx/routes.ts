import { ArrowLeftRight, BadgePercent, Search, UsersRound } from '@lucide/vue'
import { cetRoles } from '@/lib/nav'
import type { RouteRecordRaw } from 'vue-router'

// F8 · Guest side of session transfers.
export const routes: RouteRecordRaw[] = [
  {
    path: 'family/transfers',
    component: () => import('./MyTransfers.vue'),
    meta: { title: 'Session transfers', auth: 'family' },
  },
  {
    path: 'family/registrations/:id/transfer',
    component: () => import('./TransferRequest.vue'),
    props: (r) => ({ id: Number(r.params.id) }),
    meta: { title: 'Request a session transfer', auth: 'family' },
  },
]

// C1, C2, C8, C9, C10 · Staff customer service.
export const adminRoutes: RouteRecordRaw[] = [
  {
    path: 'search',
    component: () => import('./GlobalSearch.vue'),
    meta: { title: 'Search families', nav: { group: 'Front desk', label: 'Search families', icon: Search, order: 5 } },
  },
  {
    path: 'households/:id',
    component: () => import('./Household360.vue'),
    props: (r) => ({ id: Number(r.params.id) }),
    meta: { title: 'Household' },
  },
  {
    path: 'transfers',
    component: () => import('./TransferQueue.vue'),
    meta: {
      roles: cetRoles,
      title: 'Transfer requests',
      nav: { group: 'Front desk', label: 'Transfer requests', icon: ArrowLeftRight, order: 40 },
    },
  },
  {
    path: 'duplicates',
    component: () => import('./DuplicateQueue.vue'),
    meta: {
      roles: cetRoles,
      title: 'Duplicate accounts',
      nav: { group: 'Front desk', label: 'Duplicates', icon: UsersRound, order: 50 },
    },
  },
  {
    path: 'duplicates/:a/:b',
    component: () => import('./DuplicateMerge.vue'),
    props: (r) => ({ a: Number(r.params.a), b: Number(r.params.b) }),
    meta: { roles: cetRoles, title: 'Merge duplicate accounts' },
  },
  {
    path: 'discounts',
    component: () => import('./DiscountQueue.vue'),
    meta: {
      title: 'Discount approvals',
      nav: { group: 'Front desk', label: 'Discount approvals', icon: BadgePercent, order: 60 },
    },
  },
]

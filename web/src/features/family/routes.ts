import type { RouteRecordRaw } from 'vue-router'

// F2–F6. F1 (family home) stays at /family, which the shared router maps to pages/guest/Family.vue.
export const routes: RouteRecordRaw[] = [
  {
    path: 'family/members/new',
    component: () => import('./MemberProfile.vue'),
    props: (r) => ({ id: null, adult: r.query.adult === '1' }),
    meta: { title: 'Add a family member', auth: 'family' },
  },
  {
    path: 'family/members/:id(\\d+)',
    component: () => import('./MemberProfile.vue'),
    props: (r) => ({ id: Number(r.params.id), adult: false }),
    meta: { title: 'Family member', auth: 'family' },
  },
  {
    path: 'family/access',
    component: () => import('./HouseholdAccess.vue'),
    meta: { title: 'Household access', auth: 'family' },
  },
  {
    path: 'family/registrations',
    component: () => import('./MyRegistrations.vue'),
    meta: { title: 'My registrations', auth: 'family' },
  },
  {
    path: 'family/registrations/:code',
    component: () => import('./RegistrationDetail.vue'),
    props: true,
    meta: { title: 'Registration', auth: 'family' },
  },
  {
    path: 'family/registrations/:code/payments',
    component: () => import('./Payments.vue'),
    props: true,
    meta: { title: 'Payments', auth: 'family' },
  },
]

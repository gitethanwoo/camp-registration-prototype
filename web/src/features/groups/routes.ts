import type { RouteRecordRaw } from 'vue-router'

// Children of the guest site ("/"). The secure link (/g/:token) has no meta.auth: attendees
// complete forms without an account (FR-34).
export const routes: RouteRecordRaw[] = [
  {
    path: 'groups',
    component: () => import('./MyGroups.vue'),
    meta: { title: 'Your groups', auth: 'family' },
  },
  {
    path: 'groups/new/:sessionId',
    component: () => import('./GroupRoster.vue'),
    props: (r) => ({ sessionId: Number(r.params.sessionId) }),
    meta: { title: 'Register your group', auth: 'family' },
  },
  {
    path: 'groups/:id/roster',
    component: () => import('./GroupRoster.vue'),
    props: (r) => ({ groupId: Number(r.params.id) }),
    meta: { title: 'Register your group', auth: 'family' },
  },
  {
    path: 'groups/:id',
    component: () => import('./GroupTracker.vue'),
    props: (r) => ({ id: Number(r.params.id) }),
    meta: { title: 'Attendee completion', auth: 'family' },
  },
  {
    path: 'g/:token',
    component: () => import('./GroupLink.vue'),
    props: true,
    meta: { title: 'Complete your forms' },
  },
]

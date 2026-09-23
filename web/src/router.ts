import { createRouter, createWebHistory } from 'vue-router'
import AdminLayout from '@/layouts/AdminLayout.vue'
import GuestLayout from '@/layouts/GuestLayout.vue'

export const router = createRouter({
  history: createWebHistory(),
  scrollBehavior: (to, _from, saved) => saved ?? (to.hash ? { el: to.hash, top: 80 } : { top: 0 }),
  routes: [
    {
      path: '/',
      component: GuestLayout,
      children: [
        { path: '', redirect: '/programs' },
        { path: 'programs', component: () => import('@/pages/guest/Programs.vue'), meta: { title: 'Programs' } },
        { path: 'programs/:slug', component: () => import('@/pages/guest/ProgramDetail.vue'), props: true },
        { path: 'register/:sessionId', component: () => import('@/pages/guest/Register.vue'), props: r => ({ sessionId: Number(r.params.sessionId) }) },
        { path: 'confirmation/:code', component: () => import('@/pages/guest/Confirmation.vue'), props: true },
        { path: 'family', component: () => import('@/pages/guest/Family.vue'), meta: { title: 'My family' } },
        { path: 'help', component: () => import('@/pages/guest/Help.vue'), meta: { title: 'Help' } },
      ],
    },
    {
      path: '/admin',
      component: AdminLayout,
      children: [
        { path: '', component: () => import('@/pages/admin/Dashboard.vue'), meta: { title: 'Session overview' } },
        { path: 'registrations', component: () => import('@/pages/admin/Registrations.vue'), meta: { title: 'Registrations' } },
        { path: 'registrations/:id', component: () => import('@/pages/admin/RegistrationDetail.vue'), props: r => ({ id: Number(r.params.id) }), meta: { title: 'Registration' } },
        { path: 'waitlist', component: () => import('@/pages/admin/Waitlist.vue'), meta: { title: 'Waitlists' } },
        { path: 'session', component: () => import('@/pages/admin/SessionEditor.vue'), meta: { title: 'Sessions & capacity' } },
      ],
    },
    { path: '/:pathMatch(.*)*', redirect: '/programs' },
  ],
})

router.afterEach((to) => {
  document.title = to.meta.title ? `${to.meta.title as string} · WinShape Camps` : 'WinShape Camps'
})

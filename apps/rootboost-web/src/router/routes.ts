import type { RouteRecordRaw } from 'vue-router';

const routes: RouteRecordRaw[] = [
  {
    path: '/login',
    name: 'login',
    component: () => import('pages/LoginPage.vue'),
    meta: { public: true },
  },
  {
    path: '/',
    component: () => import('layouts/MainLayout.vue'),
    children: [
      { path: '', name: 'overview', component: () => import('pages/OverviewPage.vue') },
      { path: 'orders', name: 'orders', component: () => import('pages/OrdersPage.vue') },
      { path: 'campaigns', name: 'campaigns', component: () => import('pages/CampaignsPage.vue') },
      { path: 'publications', name: 'publications', component: () => import('pages/PublicationsPage.vue') },
      { path: 'studio', name: 'studio', component: () => import('pages/StudioPage.vue') },
      { path: 'brand', name: 'brandProfile', component: () => import('pages/BrandProfilePage.vue') },
    ],
  },
  {
    path: '/:catchAll(.*)*',
    component: () => import('pages/ErrorNotFound.vue'),
    meta: { public: true },
  },
];

export default routes;

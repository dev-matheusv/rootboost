import { defineRouter } from '#q-app/wrappers';
import { createRouter, createWebHistory } from 'vue-router';
import routes from './routes';
import { useAuthStore } from 'src/stores/authStore';

export default defineRouter(function ({ store }) {
  const router = createRouter({
    scrollBehavior: () => ({ left: 0, top: 0 }),
    routes,
    history: createWebHistory(process.env.VUE_ROUTER_BASE),
  });

  // Toda rota é privada, menos as marcadas como públicas.
  router.beforeEach((to) => {
    const auth = useAuthStore(store);
    if (!to.meta.public && !auth.isAuthenticated) return { name: 'login', query: { next: to.fullPath } };
    if (to.name === 'login' && auth.isAuthenticated) return { name: 'overview' };
    return true;
  });

  return router;
});

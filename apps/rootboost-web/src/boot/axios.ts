import { defineBoot } from '#q-app/wrappers';
import type { AxiosError } from 'axios';
import { api } from 'src/services/api';
import { useAuthStore } from 'src/stores/authStore';

// Token vencido ou recusado: limpa a sessão e volta pro login.
export default defineBoot(({ store, router }) => {
  const auth = useAuthStore(store);

  api.interceptors.response.use(
    (response) => response,
    async (error: AxiosError) => {
      const isLogin = error.config?.url?.includes('/auth/login');
      if (error.response?.status === 401 && !isLogin) {
        auth.logout();
        await router.replace({ name: 'login' });
      }
      return Promise.reject(error);
    },
  );
});

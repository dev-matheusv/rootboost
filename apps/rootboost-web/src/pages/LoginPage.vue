<template>
  <q-layout>
    <q-page-container>
      <!-- Um cartão só, centrado, igual em qualquer largura (o de duas colunas vazava no celular e em janela estreita). -->
      <q-page class="login-page row justify-center items-center q-pa-md">
        <q-card flat class="login-card card-rounded q-pa-lg" :class="$q.dark.isActive ? 'card-bordered-dark' : 'card-bordered-light'">
          <div class="column items-center q-mb-lg">
            <img src="/favicon.svg" alt="" width="64" height="64" />
            <div class="text-h5 sora-bold q-mt-sm">RootBoost</div>
            <div class="text-caption text-grey-6">{{ $t('app.tagline') }}</div>
          </div>
          <div class="text-subtitle1 text-weight-medium q-mb-md">{{ $t('pages.login.title') }}</div>
          <q-form class="column q-gutter-y-md" @submit.prevent="submit">
            <q-input v-model="email" type="email" autocomplete="username" :label="$t('form.email')" outlined>
              <template #prepend><q-icon :name="mdiEmailOutline" color="primary" /></template>
            </q-input>
            <q-input v-model="password" :type="showPassword ? 'text' : 'password'" autocomplete="current-password"
              :label="$t('form.password')" outlined>
              <template #prepend><q-icon :name="mdiLockOutline" color="primary" /></template>
              <template #append>
                <q-btn flat round dense :icon="showPassword ? mdiEye : mdiEyeOffOutline" @click="showPassword = !showPassword" />
              </template>
            </q-input>
            <q-banner v-if="failureMessage" rounded dense class="bg-red-1 text-negative">
              {{ failureMessage }}
            </q-banner>
            <q-btn type="submit" class="full-width" size="lg" unelevated rounded color="primary" no-caps
              :loading="auth.isLoading" :label="$t('common.actions.login')" />
          </q-form>
        </q-card>
      </q-page>
    </q-page-container>
  </q-layout>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import { useRoute, useRouter } from 'vue-router';
import { mdiEmailOutline, mdiLockOutline, mdiEye, mdiEyeOffOutline } from '@quasar/extras/mdi-v7';
import { useAuthStore, type LoginFailure } from 'src/stores/authStore';

defineOptions({ name: 'LoginPage' });

const { t } = useI18n();
const email = ref('');
const password = ref('');
const showPassword = ref(false);
const failure = ref<LoginFailure | null>(null);

const auth = useAuthStore();
const route = useRoute();
const router = useRouter();

const failureMessage = computed(() => (failure.value ? t(`pages.login.${failure.value}`) : ''));

async function submit() {
  failure.value = await auth.login(email.value, password.value);
  if (failure.value) return;
  // Só volta pra caminho interno: um "next" externo viraria redirecionamento aberto.
  const next = typeof route.query.next === 'string' && route.query.next.startsWith('/') && !route.query.next.startsWith('//')
    ? route.query.next
    : '/';
  await router.replace(next);
}
</script>

<style lang="scss" scoped>
.login-page { min-height: 100vh; background: radial-gradient(1200px 600px at 20% -10%, rgba(122, 76, 224, 0.28), transparent 60%),
  radial-gradient(900px 500px at 110% 110%, rgba(255, 122, 89, 0.14), transparent 60%); }
.login-card { width: 100%; max-width: 420px; }
/* Preenchimento automático do navegador pintava o campo de azul-claro em cima do tema escuro. */
:deep(input:-webkit-autofill), :deep(input:-webkit-autofill:hover), :deep(input:-webkit-autofill:focus) {
  -webkit-text-fill-color: currentColor;
  -webkit-box-shadow: 0 0 0 1000px transparent inset;
  transition: background-color 9999s ease-out 0s;
}
</style>

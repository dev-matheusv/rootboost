<template>
  <q-layout>
    <q-page-container>
      <q-page class="row justify-center items-center q-pa-lg">
        <q-card flat class="full-width card-rounded login-card" :class="$q.dark.isActive ? 'card-bordered-dark' : 'card-bordered-light'">
          <div class="row">
            <div :class="['col-12 col-md-5 flex flex-center brand-panel', $q.dark.isActive ? 'dark-focus' : 'light-focus']">
              <div class="text-center q-pa-xl">
                <img src="/favicon.svg" alt="RootBoost" width="120" height="120" />
                <div class="text-h3 sora-bold q-mt-md">RootBoost</div>
                <div class="text-subtitle2 q-mt-xs text-grey-7">{{ $t('app.tagline') }}</div>
              </div>
            </div>
            <div class="col-12 col-md-7 flex flex-center">
              <div class="q-pa-xl" style="width: 100%; max-width: 440px">
                <div class="text-h5 sora text-center q-mb-lg">{{ $t('pages.login.title') }}</div>
                <q-form class="q-gutter-md" @submit.prevent="submit">
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
                  <q-btn type="submit" class="full-width" size="lg" unelevated rounded color="primary"
                    :loading="auth.isLoading" :label="$t('common.actions.login')" />
                </q-form>
              </div>
            </div>
          </div>
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
.login-card { max-width: 980px; overflow: hidden; }
.brand-panel { border-radius: 15px 15px 80px 15px; }
</style>

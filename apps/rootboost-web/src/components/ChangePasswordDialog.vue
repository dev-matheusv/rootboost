<template>
  <q-dialog v-model="open" @hide="reset">
    <q-card style="width: 420px; max-width: 92vw" class="q-pa-sm">
      <q-card-section class="text-h6">{{ $t('password.title') }}</q-card-section>
      <q-card-section class="q-gutter-md">
        <q-input v-model="current" outlined type="password" autocomplete="current-password" :label="$t('password.current')" />
        <q-input v-model="next" outlined type="password" autocomplete="new-password" :label="$t('password.new')"
          :hint="$t('password.hint', { n: MIN })" />
        <q-input v-model="confirm" outlined type="password" autocomplete="new-password" :label="$t('password.confirm')"
          :error="confirm.length > 0 && confirm !== next" :error-message="$t('password.mismatch')" />
        <q-banner v-if="error" rounded class="bg-red-1 text-negative">{{ error }}</q-banner>
      </q-card-section>
      <q-card-actions align="right">
        <q-btn flat no-caps :label="$t('common.actions.cancel')" v-close-popup />
        <q-btn unelevated color="primary" no-caps :label="$t('common.actions.save')" :loading="saving" :disable="!valid" @click="save" />
      </q-card-actions>
    </q-card>
  </q-dialog>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import { useQuasar } from 'quasar';
import { isAxiosError } from 'axios';
import { api } from 'src/services/api';

defineOptions({ name: 'ChangePasswordDialog' });

const MIN = 12; // mesmo mínimo do backend (OwnerCredentials.MinLength)
const open = defineModel<boolean>({ default: false });
const { t } = useI18n();
const $q = useQuasar();

const current = ref('');
const next = ref('');
const confirm = ref('');
const saving = ref(false);
const error = ref('');

const valid = computed(() => current.value.length > 0 && next.value.length >= MIN && next.value === confirm.value);

function reset() {
  current.value = '';
  next.value = '';
  confirm.value = '';
  error.value = '';
}

async function save() {
  saving.value = true;
  error.value = '';
  try {
    await api.post('/auth/change-password', { currentPassword: current.value, newPassword: next.value });
    open.value = false;
    $q.notify({ type: 'positive', message: t('password.changed') });
  } catch (e) {
    error.value = isAxiosError(e) && typeof e.response?.data?.error === 'string'
      ? e.response.data.error
      : t('common.saveError');
  } finally {
    saving.value = false;
  }
}
</script>

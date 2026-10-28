<template>
  <q-dialog v-model="open">
    <q-card style="width: 640px; max-width: 96vw" class="q-pa-sm">
      <q-card-section class="row items-center no-wrap">
        <div class="text-h6">{{ $t('alerts.title') }}</div>
        <q-space />
        <q-btn flat dense no-caps color="primary" :label="$t('alerts.checkMeta')" :loading="checking" @click="checkMeta" />
      </q-card-section>
      <q-card-section v-if="alerts.length === 0" class="text-grey-7">{{ $t('alerts.empty') }}</q-card-section>
      <q-list v-else separator>
        <q-item v-for="a in alerts" :key="a.id">
          <q-item-section avatar><q-icon :name="icon(a.kind)" color="negative" /></q-item-section>
          <q-item-section>
            <q-item-label class="text-weight-medium">{{ $t(`alerts.kinds.${a.kind}`) }}<span v-if="a.brandKey"> · {{ a.brandKey }}</span></q-item-label>
            <q-item-label caption class="alert-message">{{ a.message }}</q-item-label>
            <q-item-label caption>
              {{ $t('alerts.seen', { when: formatDateTime(a.lastSeenAt), n: a.count }) }}
            </q-item-label>
          </q-item-section>
          <q-item-section side>
            <q-btn flat dense no-caps :label="$t('alerts.dismiss')" :loading="busy === a.id" @click="dismiss(a)" />
          </q-item-section>
        </q-item>
      </q-list>
      <q-card-section class="text-caption text-grey-7">{{ $t('alerts.hint') }}</q-card-section>
      <q-card-actions align="right">
        <q-btn flat no-caps :label="$t('common.actions.close')" v-close-popup />
      </q-card-actions>
    </q-card>
  </q-dialog>
</template>

<script setup lang="ts">
import { ref } from 'vue';
import { useI18n } from 'vue-i18n';
import { useQuasar } from 'quasar';
import { isAxiosError } from 'axios';
import { mdiCalendarAlert, mdiChartLineVariant, mdiLinkVariantOff, mdiAlertOutline } from '@quasar/extras/mdi-v7';
import { api } from 'src/services/api';
import type { OpsAlert } from 'src/types/api';
import { formatDateTime } from 'src/utils/alerts';

defineOptions({ name: 'AlertsDialog' });

const open = defineModel<boolean>({ default: false });
const alerts = defineModel<OpsAlert[]>('alerts', { default: () => [] });
const emit = defineEmits<{ changed: [] }>();
const { t } = useI18n();
const $q = useQuasar();
const busy = ref<string | null>(null);
const checking = ref(false);

function icon(kind: OpsAlert['kind']) {
  if (kind === 'PublicationFailed') return mdiCalendarAlert;
  if (kind === 'AdsSyncFailed') return mdiChartLineVariant;
  if (kind === 'MetaConnection') return mdiLinkVariantOff;
  return mdiAlertOutline;
}

function errorText(e: unknown) {
  return isAxiosError(e) && typeof e.response?.data?.error === 'string' ? e.response.data.error : t('common.saveError');
}

async function dismiss(a: OpsAlert) {
  busy.value = a.id;
  try {
    await api.post(`/admin/alerts/${a.id}/dismiss`);
    emit('changed');
  } catch (e) {
    $q.notify({ type: 'negative', message: errorText(e) });
  } finally {
    busy.value = null;
  }
}

/** Mesma checagem que roda uma vez por dia, agora. Conexão ok fecha o aviso sozinha. */
async function checkMeta() {
  checking.value = true;
  try {
    const { data } = await api.post<{ ok: boolean }[]>('/admin/alerts/check-meta');
    const ok = data.filter((r) => r.ok).length;
    $q.notify({ type: ok === data.length ? 'positive' : 'warning', message: t('alerts.checked', { ok, total: data.length }) });
    emit('changed');
  } catch (e) {
    $q.notify({ type: 'negative', message: errorText(e) });
  } finally {
    checking.value = false;
  }
}
</script>

<style lang="scss" scoped>
.alert-message { white-space: pre-line; }
</style>

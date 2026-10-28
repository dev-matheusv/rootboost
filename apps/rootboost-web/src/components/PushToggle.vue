<template>
  <!-- Avisos no celular (RB-37). Só aparece com o push ligado no servidor e num navegador que suporta. -->
  <q-item v-if="available" clickable v-ripple rounded :disable="busy" @click="toggle">
    <q-item-section avatar><q-icon :name="on ? mdiCellphoneMessage : mdiCellphoneMessageOff" :color="on ? 'primary' : undefined" /></q-item-section>
    <q-item-section v-if="!mini">{{ $t('push.label') }}</q-item-section>
    <q-item-section v-if="!mini" side><q-toggle :model-value="on" dense :disable="busy" @update:model-value="toggle" /></q-item-section>
    <q-tooltip v-if="mini" anchor="center right" self="center left">{{ $t('push.label') }}</q-tooltip>
  </q-item>
</template>

<script setup lang="ts">
import { onMounted, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import { useQuasar } from 'quasar';
import { mdiCellphoneMessage, mdiCellphoneMessageOff } from '@quasar/extras/mdi-v7';
import { api } from 'src/services/api';
import { pushSupported, urlBase64ToUint8Array } from 'src/utils/push';

defineOptions({ name: 'PushToggle' });
defineProps<{ mini: boolean }>();

const { t } = useI18n();
const $q = useQuasar();
const available = ref(false);
const on = ref(false);
const busy = ref(false);
let publicKey = '';

async function registration() {
  return pushSupported() ? await navigator.serviceWorker.getRegistration() : undefined;
}

onMounted(async () => {
  try {
    const reg = await registration();
    if (!reg) return; // sem service worker (ex.: rodando em dev): nada a ligar
    const { data } = await api.get<{ enabled: boolean; publicKey: string | null }>('/admin/push/config');
    if (!data.enabled || !data.publicKey) return;
    publicKey = data.publicKey;
    available.value = true;
    on.value = Notification.permission === 'granted' && !!(await reg.pushManager.getSubscription());
  } catch {
    // Sem push neste aparelho: o item simplesmente não aparece.
  }
});

async function toggle() {
  if (busy.value) return;
  busy.value = true;
  try {
    const reg = await registration();
    if (!reg) return;
    if (on.value) {
      const sub = await reg.pushManager.getSubscription();
      if (sub) {
        await api.post('/admin/push/devices/remove', { endpoint: sub.endpoint });
        await sub.unsubscribe();
      }
      on.value = false;
      return;
    }
    if ((await Notification.requestPermission()) !== 'granted') {
      $q.notify({ type: 'warning', message: t('push.denied') });
      return;
    }
    const sub = await reg.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey: urlBase64ToUint8Array(publicKey) });
    await api.post('/admin/push/devices', sub.toJSON());
    on.value = true;
    await api.post('/admin/push/test');
    $q.notify({ type: 'positive', message: t('push.on') });
  } catch {
    $q.notify({ type: 'negative', message: t('push.error') });
  } finally {
    busy.value = false;
  }
}
</script>

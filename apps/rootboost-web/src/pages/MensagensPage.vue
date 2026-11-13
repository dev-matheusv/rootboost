<template>
  <!-- Caixa de entrada (Onda 5c): conversas do Instagram Direct num lugar só. Só leitura por enquanto. -->
  <q-page class="q-pa-lg">
    <div class="row items-center q-mb-lg q-gutter-sm">
      <div class="col-12 col-md">
        <div class="page-eyebrow">{{ brand.selectedName }}</div>
        <div class="text-h5 sora-bold">{{ $t('inbox.title') }}</div>
        <div class="text-grey-7">{{ $t('inbox.subtitle') }}</div>
      </div>
      <q-btn v-if="brand.selected" outline color="primary" no-caps :icon="mdiRefresh" :loading="syncing" :label="$t('inbox.sync')" @click="sync" />
    </div>

    <q-banner v-if="!brand.selected" rounded class="bg-blue-1 text-primary">{{ $t('inbox.pickBrand') }}</q-banner>
    <div v-else-if="!loading && !threads.length" class="empty card-rounded q-pa-xl text-center text-grey-7">
      <q-icon :name="mdiInboxOutline" size="44px" class="q-mb-sm" />
      <div>{{ $t('inbox.empty') }}</div>
    </div>
    <div v-else class="row q-col-gutter-md">
      <div class="col-12 col-md-5" :class="{ 'gt-sm': open && $q.screen.lt.md }">
        <q-list bordered separator class="card-rounded">
          <q-item v-for="th in threads" :key="th.conversationId" clickable :active="open === th.conversationId" active-class="thread-active"
            @click="openThread(th.conversationId)">
            <q-item-section avatar>
              <q-avatar color="primary" text-color="white" size="36px">{{ initial(th.contactName) }}</q-avatar>
            </q-item-section>
            <q-item-section>
              <q-item-label class="text-weight-bold ellipsis">{{ th.contactName ? '@' + th.contactName : $t('inbox.unknown') }}</q-item-label>
              <q-item-label caption lines="1">
                <span v-if="th.lastFromBusiness">{{ $t('inbox.you') }}: </span>{{ th.lastHasAttachment ? $t('inbox.attachment') : th.lastText }}
              </q-item-label>
              <q-item-label v-if="th.canReply && !th.lastFromBusiness">
                <q-badge color="accent">{{ $t('inbox.waiting') }}</q-badge>
              </q-item-label>
            </q-item-section>
            <q-item-section side top class="text-caption">{{ when(th.lastAt) }}</q-item-section>
          </q-item>
        </q-list>
      </div>

      <div v-if="open" class="col-12 col-md-7">
        <div class="item card-rounded q-pa-md">
          <div class="row items-center q-mb-sm">
            <q-btn v-if="$q.screen.lt.md" flat round dense :icon="mdiArrowLeft" class="q-mr-sm" @click="open = ''" />
            <div class="col text-weight-bold">{{ current?.contactName ? '@' + current.contactName : $t('inbox.unknown') }}</div>
            <q-chip dense outline color="primary">Instagram</q-chip>
          </div>
          <div class="column q-gutter-y-sm chat">
            <div v-for="m in messages" :key="m.id" class="bubble q-pa-sm card-rounded" :class="m.fromBusiness ? 'me self-end' : 'them self-start'">
              <div class="text-body2" style="white-space: pre-line">
                <i v-if="m.hasAttachment" class="text-grey-7">{{ $t('inbox.attachment') }}</i>
                <template v-else>{{ m.text }}</template>
              </div>
              <div class="text-caption text-grey-6 text-right">{{ when(m.sentAt) }}</div>
            </div>
          </div>
          <q-banner rounded dense class="q-mt-md note text-grey-7">
            {{ current?.canReply ? $t('inbox.replySoon', { until: when(current.replyUntil!) }) : $t('inbox.windowClosed') }}
          </q-banner>
        </div>
      </div>
    </div>
  </q-page>
</template>

<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import { useQuasar } from 'quasar';
import { isAxiosError } from 'axios';
import { mdiArrowLeft, mdiInboxOutline, mdiRefresh } from '@quasar/extras/mdi-v7';
import { api } from 'src/services/api';
import { useBrandStore } from 'src/stores/brandStore';

defineOptions({ name: 'MensagensPage' });

interface Thread {
  channel: string; conversationId: string; contactName: string | null; lastText: string; lastHasAttachment: boolean;
  lastFromBusiness: boolean; lastAt: string; count: number; replyUntil: string | null; canReply: boolean;
}
interface Message { id: string; channel: string; contactName: string | null; fromBusiness: boolean; text: string; hasAttachment: boolean; sentAt: string }

const { t, locale } = useI18n();
const $q = useQuasar();
const brand = useBrandStore();
const threads = ref<Thread[]>([]);
const messages = ref<Message[]>([]);
const open = ref('');
const loading = ref(false);
const syncing = ref(false);
const current = computed(() => threads.value.find((x) => x.conversationId === open.value));

function initial(name: string | null) { return (name || '?').charAt(0).toUpperCase(); }
function when(iso: string) {
  return new Date(iso).toLocaleString(locale.value, { day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit' });
}
function fail(e: unknown) {
  const msg = isAxiosError(e) && typeof e.response?.data?.error === 'string' ? e.response.data.error : t('inbox.error');
  $q.notify({ type: 'negative', message: msg });
}

async function load() {
  threads.value = [];
  messages.value = [];
  open.value = '';
  if (!brand.selected) return;
  loading.value = true;
  try {
    threads.value = (await api.get<Thread[]>(`/admin/brands/${brand.selected}/inbox`)).data;
    if (threads.value.length && $q.screen.gt.sm) await openThread(threads.value[0]!.conversationId);
  } catch (e) { fail(e); } finally { loading.value = false; }
}

async function openThread(id: string) {
  open.value = id;
  try {
    messages.value = (await api.get<Message[]>(`/admin/brands/${brand.selected}/inbox/${encodeURIComponent(id)}`)).data;
  } catch (e) { fail(e); }
}

async function sync() {
  syncing.value = true;
  try {
    const { data } = await api.post<{ ok: boolean; inserted: number; error: string | null }[]>(`/admin/brands/${brand.selected}/inbox/sync`, null,
      { timeout: 120_000 });
    const failed = data.find((r) => !r.ok);
    if (failed) $q.notify({ type: 'negative', message: failed.error || t('inbox.error'), timeout: 10_000 });
    else $q.notify({ type: 'positive', message: t('inbox.synced', { n: data.reduce((s, r) => s + r.inserted, 0) }) });
    await load();
  } catch (e) { fail(e); } finally { syncing.value = false; }
}

watch(() => brand.selected, () => void load());
onMounted(() => void load());
</script>

<style scoped lang="scss">
.chat { max-height: 60vh; overflow-y: auto; }
.bubble { max-width: 85%; border: 1px solid rgba(0, 0, 0, 0.08); }
.me { background: rgba(122, 76, 224, 0.12); }
.note { border: 1px dashed rgba(122, 76, 224, 0.4); background: transparent; }
.thread-active { background: rgba(122, 76, 224, 0.08); }
.body--dark .bubble { border-color: rgba(255, 255, 255, 0.08); }
</style>

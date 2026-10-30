<template>
  <!-- Cadastro do produto por conversa (RB-60): a IA pergunta, lê o site e propõe; a pessoa confere e salva. -->
  <div class="row q-col-gutter-lg">
    <div class="col-12 col-md-6">
      <div class="chat card-rounded column no-wrap">
        <div class="messages col q-pa-md column no-wrap q-gutter-y-sm" ref="scrollEl">
          <div v-for="(m, i) in messages" :key="i" class="bubble" :class="m.role">{{ m.text }}</div>
          <div v-if="thinking" class="bubble assistant row items-center q-gutter-x-sm">
            <q-spinner-dots size="20px" color="primary" /><span class="text-caption">{{ siteHint ? $t('onboarding.readingSite') : $t('onboarding.thinking') }}</span>
          </div>
        </div>
        <q-separator />
        <div class="q-pa-sm row no-wrap items-end q-gutter-x-sm">
          <q-input v-model="draft" class="col" dense borderless autogrow :disable="!aiEnabled || thinking"
            :placeholder="aiEnabled ? $t('onboarding.placeholder') : $t('onboarding.off')" @keydown.enter.exact.prevent="send" />
          <DictateButton v-model="draft" :disable="!aiEnabled || thinking" />
          <q-btn round unelevated color="primary" :icon="mdiSend" :disable="!draft.trim() || thinking || !aiEnabled" @click="send" />
        </div>
      </div>
    </div>

    <div class="col-12 col-md-6">
      <div class="proposal card-rounded q-pa-md column q-gutter-y-sm">
        <div class="row items-center">
          <div class="text-subtitle1 text-weight-bold col">{{ $t('onboarding.form') }}</div>
          <q-chip v-if="savedAt" dense color="positive" text-color="white" :icon="mdiCheck">{{ $t('onboarding.saved') }}</q-chip>
        </div>
        <div class="text-caption text-grey-7">{{ $t('onboarding.formHint') }}</div>
        <q-input v-model="form.name" outlined dense :label="$t('onboarding.f.name')" />
        <q-select v-model="form.kind" outlined dense emit-value map-options :options="kinds" :label="$t('onboarding.f.kind')" />
        <q-input v-model="form.summary" outlined dense autogrow :label="$t('onboarding.f.summary')" />
        <q-input v-model="form.audience" outlined dense autogrow :label="$t('onboarding.f.audience')" />
        <q-input v-model="form.url" outlined dense :label="$t('onboarding.f.url')" />
        <q-input v-if="form.kind === 'app' || form.kind === 'site'" v-model="form.appUrl" outlined dense
          :label="$t('onboarding.f.appUrl')" :hint="$t('onboarding.f.appUrlHint')" />
        <q-select v-model="form.features" outlined dense multiple use-chips use-input new-value-mode="add-unique" hide-dropdown-icon
          :label="$t('onboarding.f.features')" :hint="$t('onboarding.f.listHint')" />
        <q-select v-model="form.differentials" outlined dense multiple use-chips use-input new-value-mode="add-unique" hide-dropdown-icon
          :label="$t('onboarding.f.differentials')" />
        <q-input v-model="form.offer" outlined dense :label="$t('onboarding.f.offer')" />
        <div class="row items-center q-gutter-sm q-pt-sm">
          <q-btn unelevated color="primary" no-caps :icon="mdiContentSave" :label="$t('onboarding.save')" :loading="saving"
            :disable="!form.name.trim()" @click="save" />
          <span v-if="!readyToSave(form)" class="text-caption text-grey-7">{{ $t('onboarding.missing') }}</span>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { nextTick, onMounted, reactive, ref } from 'vue';
import DictateButton from 'src/components/DictateButton.vue';
import { useI18n } from 'vue-i18n';
import { useQuasar } from 'quasar';
import { isAxiosError } from 'axios';
import { mdiCheck, mdiContentSave, mdiSend } from '@quasar/extras/mdi-v7';
import { api } from 'src/services/api';
import { emptyProduct, mergeProposal, readyToSave, type ProductForm, type ProductProposal } from 'src/utils/onboarding';

defineOptions({ name: 'ProductOnboarding' });
const props = defineProps<{ brandKey: string }>();

const { t } = useI18n();
const $q = useQuasar();
const messages = ref<{ role: 'user' | 'assistant'; text: string }[]>([]);
const draft = ref('');
const thinking = ref(false);
const siteHint = ref(false);
const saving = ref(false);
const savedAt = ref<string | null>(null);
const aiEnabled = ref(false);
const form = reactive<ProductForm>(emptyProduct());
const scrollEl = ref<HTMLElement | null>(null);
const base = `/admin/brands/${props.brandKey}`;

const kinds = ['app', 'site', 'loja', 'servico', 'outro'].map((k) => ({ value: k, label: t(`onboarding.kinds.${k}`) }));

function apiError(e: unknown): string {
  if (isAxiosError(e) && typeof e.response?.data?.error === 'string') return e.response.data.error;
  return t('common.saveError');
}

async function scrollDown() {
  await nextTick();
  scrollEl.value?.scrollTo({ top: scrollEl.value.scrollHeight, behavior: 'smooth' });
}

/** Manda a conversa inteira; a IA responde com a próxima pergunta e a proposta de cadastro atualizada. */
async function ask() {
  thinking.value = true;
  await scrollDown();
  try {
    const { data } = await api.post<{ reply: string; proposal: ProductProposal; done: boolean }>(`${base}/onboarding/chat`,
      { messages: messages.value }, { timeout: 2 * 60_000 });
    messages.value.push({ role: 'assistant', text: data.reply });
    Object.assign(form, mergeProposal(form, data.proposal));
  } catch (e) {
    $q.notify({ type: 'negative', message: apiError(e) });
  } finally {
    thinking.value = false;
    siteHint.value = false;
    await scrollDown();
  }
}

async function send() {
  const text = draft.value.trim();
  if (!text || thinking.value) return;
  messages.value.push({ role: 'user', text });
  draft.value = '';
  siteHint.value = /https?:\/\/|\.(com|br|app|io|net|org)\b/i.test(text);
  await ask();
}

async function save() {
  saving.value = true;
  try {
    const { data } = await api.put<{ updatedAt: string }>(`${base}/product`, { ...form });
    savedAt.value = data.updatedAt;
    $q.notify({ type: 'positive', message: t('onboarding.savedHint') });
  } catch (e) {
    $q.notify({ type: 'negative', message: apiError(e) });
  } finally {
    saving.value = false;
  }
}

onMounted(async () => {
  try {
    const [status, product] = await Promise.all([
      api.get<{ enabled: boolean }>(`${base}/ai/status`),
      api.get<ProductForm & { updatedAt: string }>(`${base}/product`),
    ]);
    aiEnabled.value = status.data.enabled;
    if (product.status === 200 && product.data) {
      Object.assign(form, { ...emptyProduct(), ...product.data, url: product.data.url ?? '', appUrl: product.data.appUrl ?? '', offer: product.data.offer ?? '' });
      savedAt.value = product.data.updatedAt;
    }
  } catch {
    // Sem status, a conversa fica desligada e o formulário continua funcionando.
  }
  if (aiEnabled.value) await ask(); // a IA começa a conversa
});
</script>

<style lang="scss" scoped>
.chat, .proposal { border: 1px solid rgba(0, 0, 0, 0.08); background: #fff; }
.body--dark .chat, .body--dark .proposal { border-color: rgba(255, 255, 255, 0.08); background: $dark; }
.chat { height: 560px; }
.messages { overflow-y: auto; }
.bubble { max-width: 85%; padding: 8px 12px; border-radius: 14px; white-space: pre-line; line-height: 1.4; }
.bubble.assistant { align-self: flex-start; background: $light-focus; border-bottom-left-radius: 4px; }
.body--dark .bubble.assistant { background: $dark-focus; }
.bubble.user { align-self: flex-end; background: $primary; color: #fff; border-bottom-right-radius: 4px; }
</style>

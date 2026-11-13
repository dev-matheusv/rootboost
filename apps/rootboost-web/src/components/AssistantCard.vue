<template>
  <!-- Pergunte à IA (13/11): conversa sobre os números da marca. A IA só responde e sugere; nada é publicado nem gasto. -->
  <q-card flat bordered class="card-rounded q-pa-md q-mb-xl">
    <div class="row items-center q-gutter-sm q-mb-sm">
      <q-icon :name="mdiCreationOutline" color="primary" size="22px" />
      <div class="col">
        <div class="text-subtitle1 text-weight-bold">{{ $t('assistant.title') }}</div>
        <div class="text-caption text-grey-7">{{ $t('assistant.subtitle') }}</div>
      </div>
      <q-btn v-if="turns.length" flat dense no-caps color="primary" :label="$t('assistant.clear')" @click="turns = []" />
    </div>

    <div v-if="!turns.length" class="row q-gutter-xs q-mb-sm">
      <q-chip v-for="q in examples" :key="q" clickable outline color="primary" @click="ask(q)">{{ q }}</q-chip>
    </div>

    <div ref="list" class="column q-gutter-y-sm q-mb-sm chat">
      <div v-for="(m, i) in turns" :key="i" class="bubble q-pa-sm card-rounded" :class="m.role === 'user' ? 'me self-end' : 'ai self-start'">
        <div class="text-body2" style="white-space: pre-line">{{ m.text }}</div>
      </div>
      <div v-if="thinking" class="bubble ai self-start q-pa-sm card-rounded text-grey-7">{{ $t('assistant.thinking') }}</div>
    </div>

    <q-input v-model="draft" outlined dense autogrow :placeholder="$t('assistant.placeholder')" :disable="thinking"
      @keydown.enter.exact.prevent="ask(draft)">
      <template #append>
        <DictateButton v-model="draft" />
        <q-btn flat round dense color="primary" :icon="mdiSend" :loading="thinking" :disable="!draft.trim()" @click="ask(draft)" />
      </template>
    </q-input>
  </q-card>
</template>

<script setup lang="ts">
import { computed, nextTick, ref } from 'vue';
import { useQuasar } from 'quasar';
import { useI18n } from 'vue-i18n';
import { isAxiosError } from 'axios';
import { mdiCreationOutline, mdiSend } from '@quasar/extras/mdi-v7';
import { api } from 'src/services/api';
import DictateButton from 'src/components/DictateButton.vue';

defineOptions({ name: 'AssistantCard' });

const props = defineProps<{ brandKey: string }>();
const { t } = useI18n();
const $q = useQuasar();
const turns = ref<{ role: 'user' | 'assistant'; text: string }[]>([]);
const draft = ref('');
const thinking = ref(false);
const list = ref<HTMLElement | null>(null);
const examples = computed(() => [t('assistant.ex1'), t('assistant.ex2'), t('assistant.ex3')]);

async function ask(text: string) {
  const q = text.trim();
  if (!q || thinking.value) return;
  turns.value.push({ role: 'user', text: q });
  draft.value = '';
  thinking.value = true;
  try {
    const { data } = await api.post<{ answer: string }>(`/admin/brands/${props.brandKey}/assistant/chat`,
      { messages: turns.value.slice(-12) }, { timeout: 120_000 });
    turns.value.push({ role: 'assistant', text: data.answer || t('assistant.empty') });
  } catch (e) {
    turns.value.pop();
    draft.value = q;
    const msg = isAxiosError(e) && typeof e.response?.data?.error === 'string' ? e.response.data.error : t('assistant.error');
    $q.notify({ type: 'negative', message: msg });
  } finally {
    thinking.value = false;
    await nextTick();
    list.value?.lastElementChild?.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
  }
}
</script>

<style scoped lang="scss">
.chat { max-height: 420px; overflow-y: auto; }
.bubble { max-width: 88%; border: 1px solid rgba(0, 0, 0, 0.08); }
.me { background: rgba(122, 76, 224, 0.12); }
.body--dark .bubble { border-color: rgba(255, 255, 255, 0.08); }
</style>

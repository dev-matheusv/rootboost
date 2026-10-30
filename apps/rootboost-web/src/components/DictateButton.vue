<template>
  <!-- Falar em vez de digitar: o navegador transcreve (Chrome, Edge, Android e iPhone); sem custo de IA. -->
  <q-btn v-if="supported" flat round dense :color="listening ? 'negative' : 'primary'" :icon="listening ? mdiStop : mdiMicrophoneOutline"
    :class="{ pulse: listening }" @click="toggle">
    <q-tooltip>{{ listening ? $t('dictate.stop') : $t('dictate.start') }}</q-tooltip>
  </q-btn>
</template>

<script setup lang="ts">
import { onBeforeUnmount, ref } from 'vue';
import { useQuasar } from 'quasar';
import { useI18n } from 'vue-i18n';
import { mdiMicrophoneOutline, mdiStop } from '@quasar/extras/mdi-v7';

const props = defineProps<{ modelValue: string }>();
const emit = defineEmits<{ 'update:modelValue': [string] }>();
const { t } = useI18n();
const $q = useQuasar();

interface Recognition {
  lang: string; continuous: boolean; interimResults: boolean;
  onresult: ((e: { resultIndex: number; results: ArrayLike<{ isFinal: boolean; 0: { transcript: string } }> }) => void) | null;
  onend: (() => void) | null; onerror: ((e: { error: string }) => void) | null;
  start(): void; stop(): void;
}
const Ctor = (window as unknown as { SpeechRecognition?: new () => Recognition; webkitSpeechRecognition?: new () => Recognition })
  .SpeechRecognition ?? (window as unknown as { webkitSpeechRecognition?: new () => Recognition }).webkitSpeechRecognition;
const supported = !!Ctor;
const listening = ref(false);
let rec: Recognition | null = null;
let base = '';

function toggle() {
  if (listening.value) {
    rec?.stop();
    return;
  }
  if (!Ctor) return;
  rec = new Ctor();
  rec.lang = 'pt-BR';
  rec.continuous = true;
  rec.interimResults = true;
  base = props.modelValue ? `${props.modelValue.trimEnd()} ` : '';
  rec.onresult = (e) => {
    let text = '';
    for (let i = 0; i < e.results.length; i++) text += e.results[i]![0].transcript;
    emit('update:modelValue', base + text.trim());
  };
  rec.onerror = (e) => {
    if (e.error === 'not-allowed') $q.notify({ type: 'negative', message: t('dictate.denied') });
  };
  rec.onend = () => (listening.value = false);
  rec.start();
  listening.value = true;
}

onBeforeUnmount(() => rec?.stop());
</script>

<style lang="scss" scoped>
.pulse { animation: pulse 1.2s ease-in-out infinite; }
@keyframes pulse { 0%, 100% { opacity: 1; } 50% { opacity: 0.45; } }
@media (prefers-reduced-motion: reduce) { .pulse { animation: none; } }
</style>

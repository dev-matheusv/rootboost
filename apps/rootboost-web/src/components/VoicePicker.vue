<template>
  <div class="column q-gutter-y-sm">
    <!-- Filtros só aparecem quando há o que filtrar. -->
    <div v-if="voices.length > 3" class="row q-gutter-xs">
      <q-chip v-for="f in filters" :key="f.value" clickable dense :outline="filter !== f.value" color="primary"
        :text-color="filter === f.value ? 'white' : 'primary'" @click="filter = filter === f.value ? null : f.value">{{ $t(f.label) }}</q-chip>
    </div>
    <div class="grid">
      <!-- Voz padrão do estúdio (a que o worker usa sem escolha). -->
      <div class="voice card-rounded q-pa-sm" :class="{ selected: modelValue === '' }" role="radio" tabindex="0"
        :aria-checked="modelValue === ''" @click="emit('update:modelValue', '')" @keyup.enter="emit('update:modelValue', '')">
        <div class="row items-center no-wrap q-gutter-x-xs">
          <q-icon :name="mdiStarOutline" color="primary" size="20px" />
          <div class="text-weight-bold ellipsis">{{ $t('voicePicker.default') }}</div>
        </div>
        <div class="text-caption text-grey-7">{{ $t('voicePicker.defaultHint') }}</div>
      </div>

      <div v-for="v in shown" :key="v.key" class="voice card-rounded q-pa-sm" :class="{ selected: modelValue === v.key, playing: playing === v.key }"
        role="radio" tabindex="0" :aria-checked="modelValue === v.key" @click="emit('update:modelValue', v.key)" @keyup.enter="emit('update:modelValue', v.key)">
        <div class="row items-center no-wrap q-gutter-x-xs">
          <q-btn round dense flat size="sm" color="primary" :icon="playing === v.key ? mdiStop : mdiPlay" :loading="loadingKey === v.key"
            :aria-label="$t('voicePicker.play')" @click.stop="toggle(v)" />
          <div class="text-weight-bold ellipsis col">{{ v.name }}</div>
        </div>
        <div class="row q-gutter-xs q-mt-xs">
          <q-badge outline color="primary">{{ $t(v.kind === 'Clone' ? 'voicePicker.brandVoice' : 'voicePicker.bank') }}</q-badge>
          <q-badge v-for="tag in tags(v)" :key="tag" outline color="grey-7">{{ tag }}</q-badge>
        </div>
        <div v-if="playing === v.key" class="wave q-mt-xs" aria-hidden="true"><span v-for="n in 12" :key="n" /></div>
      </div>

      <!-- Gravar a própria voz: leva pra página Vozes. -->
      <router-link to="/vozes" class="voice add card-rounded q-pa-sm column flex-center text-center">
        <q-icon :name="mdiMicrophoneOutline" color="primary" size="22px" />
        <div class="text-weight-bold">{{ $t('voices.record') }}</div>
      </router-link>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, onBeforeUnmount, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import { mdiMicrophoneOutline, mdiPlay, mdiStarOutline, mdiStop } from '@quasar/extras/mdi-v7';
import { api } from 'src/services/api';
import type { PickableVoice } from 'src/types/api';

const props = defineProps<{ modelValue: string; voices: PickableVoice[]; brandKey: string }>();
const emit = defineEmits<{ 'update:modelValue': [string] }>();
const { t } = useI18n();

const filters = [
  { value: 'female', label: 'voices.female' },
  { value: 'male', label: 'voices.male' },
  { value: 'Clone', label: 'voicePicker.brandVoice' },
  { value: 'Design', label: 'voicePicker.bank' },
];
const filter = ref<string | null>(null);
const shown = computed(() => props.voices.filter((v) => !filter.value || v.kind === filter.value || (v.instruct ?? '').startsWith(`${filter.value},`)));

/** "female, middle-aged, low pitch" vira selos curtos em português. */
function tags(v: PickableVoice): string[] {
  if (!v.instruct) return [];
  const [gender, age, pitch] = v.instruct.split(',').map((s) => s.trim());
  return [gender ? t(`voices.${gender}`) : '', age ? t(`voices.age.${age}`) : '', pitch ? t(`voices.pitch.${pitch}`) : ''].filter(Boolean);
}

// Uma amostra por vez: tocar outra para a anterior.
const audio = new Audio();
const urls = new Map<string, string>();
const playing = ref<string | null>(null);
const loadingKey = ref<string | null>(null);
audio.onended = () => (playing.value = null);

async function toggle(v: PickableVoice) {
  if (playing.value === v.key) {
    audio.pause();
    playing.value = null;
    return;
  }
  const seed = v.chosenSeed ?? v.samples[0];
  if (!seed) return;
  let url = urls.get(v.key);
  if (!url) {
    loadingKey.value = v.key;
    try {
      const { data } = await api.get<Blob>(`/admin/brands/${props.brandKey}/voices/${v.id}/samples/${seed}`, { responseType: 'blob' });
      url = URL.createObjectURL(data);
      urls.set(v.key, url);
    } finally {
      loadingKey.value = null;
    }
  }
  audio.src = url;
  await audio.play();
  playing.value = v.key;
}

onBeforeUnmount(() => {
  audio.pause();
  urls.forEach((u) => URL.revokeObjectURL(u));
});
</script>

<style lang="scss" scoped>
.grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(150px, 1fr)); gap: 8px; }
.voice { border: 1px solid rgba(0, 0, 0, 0.12); cursor: pointer; transition: border-color 0.15s, background 0.15s, transform 0.15s; min-height: 72px; }
.voice:focus-visible { outline: 2px solid $primary; outline-offset: 2px; }
.voice.selected { border-color: $primary; background: rgba(122, 76, 224, 0.1); transform: scale(1.02); }
.voice.add { border-style: dashed; color: inherit; text-decoration: none; }
.body--dark .voice { border-color: rgba(255, 255, 255, 0.12); }
.body--dark .voice.selected { border-color: $primary; background: rgba(122, 76, 224, 0.18); }
.wave { display: flex; align-items: flex-end; gap: 2px; height: 14px; }
.wave span { flex: 1; background: $primary; border-radius: 1px; animation: wave 0.9s ease-in-out infinite; }
.wave span:nth-child(odd) { animation-delay: 0.15s; }
.wave span:nth-child(3n) { animation-delay: 0.3s; }
@keyframes wave { 0%, 100% { height: 25%; } 50% { height: 100%; } }
@media (prefers-reduced-motion: reduce) { .wave span { animation: none; height: 60%; } .voice.selected { transform: none; } }
</style>

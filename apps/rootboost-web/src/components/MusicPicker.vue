<template>
  <div class="column q-gutter-y-sm">
    <div class="text-caption text-grey-7">{{ $t('musicPicker.hint') }}</div>
    <div class="row q-gutter-xs">
      <q-chip v-for="c in moods" :key="c" clickable dense outline color="primary" @click="search(c)">{{ $t(`musicPicker.moods.${c}`) }}</q-chip>
    </div>
    <q-input v-model="query" outlined dense :label="$t('musicPicker.search')" @keyup.enter="search(query)">
      <template #append><q-btn flat round dense :icon="mdiMagnify" :loading="loading" :aria-label="$t('musicPicker.search')" @click="search(query)" /></template>
    </q-input>
    <div v-if="error" class="text-negative text-caption">{{ error }}</div>

    <!-- Escolhida: mostra mesmo antes de buscar (vem da publicação salva). -->
    <div v-if="modelValue.audioId" class="row items-center q-gutter-x-sm">
      <q-chip removable color="primary" text-color="white" :icon="mdiMusicNote" @remove="pick(null)">{{ modelValue.title || modelValue.audioId }}</q-chip>
    </div>

    <div v-if="tracks.length" class="grid">
      <div v-for="tr in tracks" :key="tr.id" class="track card-rounded q-pa-sm" :class="{ selected: modelValue.audioId === tr.id }" role="radio"
        tabindex="0" :aria-checked="modelValue.audioId === tr.id" @click="pick(tr)" @keyup.enter="pick(tr)">
        <div class="row items-center no-wrap q-gutter-x-xs">
          <q-btn v-if="tr.previewUrl" round dense flat size="sm" color="primary" :icon="playing === tr.id ? mdiStop : mdiPlay"
            :aria-label="$t('musicPicker.play')" @click.stop="toggle(tr)" />
          <q-icon v-else :name="mdiMusicNote" color="grey-6" size="20px" />
          <div class="col ellipsis">
            <div class="text-weight-bold ellipsis">{{ tr.title }}</div>
            <div class="text-caption text-grey-7 ellipsis">{{ tr.artist }} · {{ duration(tr.durationMs) }}</div>
          </div>
        </div>
      </div>
    </div>
    <div v-else-if="searched && !loading" class="text-caption text-grey-7">{{ $t('musicPicker.none') }}</div>

    <template v-if="modelValue.audioId">
      <div>
        <div class="text-caption">{{ $t('musicPicker.musicVolume') }}: {{ modelValue.musicVolume }}</div>
        <q-slider :model-value="modelValue.musicVolume" :min="1" :max="100" color="primary"
          @update:model-value="(v) => emit('update:modelValue', { ...modelValue, musicVolume: v ?? 80 })" />
      </div>
      <div>
        <div class="text-caption">{{ $t('musicPicker.videoVolume') }}: {{ modelValue.videoVolume }}</div>
        <q-slider :model-value="modelValue.videoVolume" :min="1" :max="100" color="primary"
          @update:model-value="(v) => emit('update:modelValue', { ...modelValue, videoVolume: v ?? 100 })" />
        <div class="text-caption text-grey-7">{{ $t('musicPicker.videoVolumeHint') }}</div>
      </div>
      <div class="text-caption text-grey-7">{{ $t('musicPicker.noPreview') }}</div>
    </template>
  </div>
</template>

<script setup lang="ts">
import { onBeforeUnmount, ref } from 'vue';
import { isAxiosError } from 'axios';
import { mdiMagnify, mdiMusicNote, mdiPlay, mdiStop } from '@quasar/extras/mdi-v7';
import { api } from 'src/services/api';
import type { MusicChoice, MusicTrack } from 'src/types/api';

defineOptions({ name: 'MusicPicker' });

const props = defineProps<{ modelValue: MusicChoice; brandKey: string }>();
const emit = defineEmits<{ 'update:modelValue': [value: MusicChoice] }>();

/** Atalhos de clima (a busca do Instagram é por palavra; em inglês acha mais). */
const moods = ['calm', 'happy', 'travel', 'corporate', 'emotional'] as const;
const query = ref('');
const tracks = ref<MusicTrack[]>([]);
const loading = ref(false);
const searched = ref(false);
const error = ref('');

async function search(q: string) {
  query.value = q;
  loading.value = true;
  error.value = '';
  try {
    const { data } = await api.get<MusicTrack[]>(`/admin/brands/${props.brandKey}/publications/music`, { params: { q: q.trim() || undefined } });
    tracks.value = data;
  } catch (e) {
    tracks.value = [];
    error.value = isAxiosError(e) && typeof e.response?.data?.error === 'string' ? e.response.data.error : String(e);
  } finally {
    loading.value = false;
    searched.value = true;
  }
}

function pick(tr: MusicTrack | null) {
  emit('update:modelValue', tr
    ? { ...props.modelValue, audioId: tr.id, title: tr.artist ? `${tr.title} · ${tr.artist}` : tr.title }
    : { ...props.modelValue, audioId: null, title: null });
}

function duration(ms: number): string {
  const s = Math.round(ms / 1000);
  return `${Math.floor(s / 60)}:${String(s % 60).padStart(2, '0')}`;
}

// Prévia: link temporário do Instagram, tocado no navegador.
const playing = ref<string | null>(null);
const audio = new Audio();
audio.onended = () => (playing.value = null);

async function toggle(tr: MusicTrack) {
  if (playing.value === tr.id) {
    audio.pause();
    playing.value = null;
    return;
  }
  audio.src = tr.previewUrl ?? '';
  playing.value = tr.id;
  try {
    await audio.play();
  } catch {
    playing.value = null;
  }
}

onBeforeUnmount(() => audio.pause());
</script>

<style lang="scss" scoped>
.grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(180px, 1fr)); gap: 8px; }
.track { border: 1px solid rgba(0, 0, 0, 0.12); cursor: pointer; transition: border-color 0.15s, background 0.15s; }
.track:focus-visible { outline: 2px solid $primary; outline-offset: 2px; }
.track.selected { border-color: $primary; background: rgba(122, 76, 224, 0.1); }
.body--dark .track { border-color: rgba(255, 255, 255, 0.12); }
.body--dark .track.selected { border-color: $primary; background: rgba(122, 76, 224, 0.18); }
</style>

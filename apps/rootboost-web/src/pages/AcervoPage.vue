<template>
  <q-page class="q-pa-lg">
    <div class="row items-center q-mb-lg q-gutter-sm">
      <div class="col-12 col-md">
        <div class="page-eyebrow">{{ brand.selectedName }}</div>
        <div class="text-h5 sora-bold">{{ $t('acervo.title') }}</div>
        <div class="text-grey-7">{{ $t('acervo.subtitle') }}</div>
      </div>
      <q-file v-if="brand.selected" :model-value="null" multiple dense outlined accept=".jpg,.jpeg,.png,.webp,.mp4,.mov" class="upload"
        :label="$t('acervo.upload')" :loading="uploading" @update:model-value="upload">
        <template #prepend><q-icon :name="mdiCloudUploadOutline" /></template>
      </q-file>
    </div>

    <q-banner v-if="!brand.selected" rounded class="bg-blue-1 text-primary">{{ $t('acervo.pickBrand') }}</q-banner>
    <div v-else-if="!loading && !items.length" class="empty card-rounded q-pa-xl text-center text-grey-7">
      <q-icon :name="mdiImageMultipleOutline" size="44px" class="q-mb-sm" />
      <div>{{ $t('acervo.empty') }}</div>
    </div>
    <div v-else class="row q-col-gutter-md">
      <div v-for="m in items" :key="m.id" class="col-12 col-sm-6 col-md-4 col-lg-3">
        <div class="item card-rounded column no-wrap">
          <video v-if="m.isVideo" :src="m.url" preload="metadata" controls playsinline class="thumb" />
          <div v-else-if="m.hidesOriginal && !m.redacted" class="thumb column flex-center text-center q-pa-md text-grey-5">
            <q-icon :name="mdiShieldAlertOutline" size="36px" color="warning" />
            <div class="text-caption q-mt-sm">{{ $t('privacy.needsBlur') }}</div>
          </div>
          <img v-else :src="m.url" alt="" class="thumb" loading="lazy" />
          <div class="q-pa-sm column q-gutter-y-xs">
            <div v-if="m.personalData !== 'None' && (m.personalData !== 'Unchecked' || blockedReason(m))" class="row items-center q-gutter-xs">
              <q-chip v-if="m.personalData !== 'Unchecked'" dense size="sm" :color="m.personalData === 'Fictional' ? 'blue-grey-6' : 'warning'" text-color="white" :icon="mdiShieldAccountOutline">
                {{ $t(`privacy.badge.${m.personalData}`) }}<span v-if="m.personalDataKinds">: {{ m.personalDataKinds }}</span>
              </q-chip>
              <q-chip v-if="blockedReason(m)" dense size="sm" color="negative" text-color="white">{{ blockedReason(m) }}</q-chip>
            </div>
            <q-input v-model="m.description" dense borderless autogrow :placeholder="$t('acervo.describeHint')"
              @blur="saveDescription(m)" />
            <q-input v-model="m.tags" dense borderless :placeholder="$t('acervo.tags')" class="text-caption" @blur="saveDescription(m)" />
            <div class="row items-center no-wrap">
              <q-btn v-if="!m.isVideo && aiEnabled" flat dense no-caps size="sm" color="primary" :icon="mdiCreationOutline"
                :label="$t('acervo.describe')" :loading="busy === m.id" @click="describe(m)" />
              <q-btn v-if="!m.isVideo" flat dense no-caps size="sm" color="primary" :icon="mdiBlurLinear" :label="$t('privacy.adjust')"
                @click="editing = m" />
              <q-space />
              <q-btn flat round dense size="sm" :icon="mdiDeleteOutline" color="negative" @click="remove(m)">
                <q-tooltip>{{ $t('acervo.delete') }}</q-tooltip>
              </q-btn>
            </div>
          </div>
        </div>
      </div>
    </div>
    <RedactionEditor :model-value="!!editing" :media="editing" :base="base()" @update:model-value="(v) => { if (!v) editing = null }"
      @saved="(d) => editing && Object.assign(editing, d)" />
  </q-page>
</template>

<script setup lang="ts">
import { onMounted, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import { useQuasar } from 'quasar';
import { isAxiosError } from 'axios';
import {
  mdiBlurLinear, mdiCloudUploadOutline, mdiCreationOutline, mdiDeleteOutline, mdiImageMultipleOutline, mdiShieldAccountOutline, mdiShieldAlertOutline,
} from '@quasar/extras/mdi-v7';
import { api } from 'src/services/api';
import RedactionEditor from 'src/components/RedactionEditor.vue';
import { useBrandStore } from 'src/stores/brandStore';

defineOptions({ name: 'AcervoPage' });

interface Media {
  id: string; url: string; isVideo: boolean; fileName: string; description: string | null; tags: string | null;
  personalData: 'Unchecked' | 'None' | 'Fictional' | 'Real' | 'Uncertain'; personalDataKinds: string | null;
  redactionBoxes: { x: number; y: number; w: number; h: number }[]; redacted: boolean; hidesOriginal: boolean;
}

const { t } = useI18n();
const $q = useQuasar();
const brand = useBrandStore();
const items = ref<Media[]>([]);
const loading = ref(false);
const uploading = ref(false);
const busy = ref<string | null>(null);
const aiEnabled = ref(false);
const policy = ref<'Blur' | 'FictionalOnly'>('Blur');
const editing = ref<Media | null>(null);

/** Mesma regra do servidor: real em marca "só fictício" nunca entra; real ou dúvida só com a versão borrada. */
function blockedReason(m: Media): string | null {
  if (m.personalData === 'Real' && policy.value === 'FictionalOnly') return t('privacy.blockedFictionalOnly');
  if ((m.personalData === 'Real' || m.personalData === 'Uncertain') && !m.redacted) return t('privacy.blockedNoBlur');
  if (m.personalData === 'Unchecked' && !m.isVideo) return t('privacy.blockedUnchecked');
  return null;
}
const base = () => `/admin/brands/${brand.selected}/media`;

function apiError(e: unknown): string {
  if (isAxiosError(e) && typeof e.response?.data?.error === 'string') return e.response.data.error;
  return t('common.saveError');
}

async function load() {
  if (!brand.selected) return;
  loading.value = true;
  try {
    const [media, ai, privacy] = await Promise.all([api.get<Media[]>(base()), api.get<{ enabled: boolean }>(`/admin/brands/${brand.selected}/ai/status`),
      api.get<{ policy: 'Blur' | 'FictionalOnly' }>(`/admin/brands/${brand.selected}/privacy`)]);
    items.value = media.data;
    aiEnabled.value = ai.data.enabled;
    policy.value = privacy.data.policy;
  } catch {
    $q.notify({ type: 'negative', message: t('common.error') });
  } finally {
    loading.value = false;
  }
}

/** Sobe cada arquivo e, se for foto e a IA estiver ligada, já pede a descrição (a pessoa só confere). */
async function upload(files: File[] | File | null) {
  const list = Array.isArray(files) ? files : files ? [files] : [];
  if (!list.length) return;
  uploading.value = true;
  try {
    for (const f of list) {
      const body = new FormData();
      body.append('file', f);
      const { data } = await api.post<Media>(base(), body, { timeout: 15 * 60_000 });
      items.value.unshift(data);
      if (!data.isVideo && aiEnabled.value) void describe(items.value[0]!);
    }
  } catch (e) {
    $q.notify({ type: 'negative', message: apiError(e) });
  } finally {
    uploading.value = false;
  }
}

async function describe(m: Media) {
  busy.value = m.id;
  try {
    const { data } = await api.post<Media>(`${base()}/${m.id}/describe`);
    Object.assign(m, data);
  } catch (e) {
    $q.notify({ type: 'negative', message: apiError(e) });
  } finally {
    busy.value = null;
  }
}

async function saveDescription(m: Media) {
  try {
    await api.put(`${base()}/${m.id}/description`, { description: m.description, tags: m.tags });
  } catch (e) {
    $q.notify({ type: 'negative', message: apiError(e) });
  }
}

function remove(m: Media) {
  $q.dialog({ title: t('acervo.delete'), message: t('acervo.deleteConfirm'), cancel: true }).onOk(() => {
    void api.delete(`${base()}/${m.id}`)
      .then(() => (items.value = items.value.filter((x) => x.id !== m.id)))
      .catch((e: unknown) => $q.notify({ type: 'negative', message: apiError(e) }));
  });
}

onMounted(load);
watch(() => brand.selected, () => void load());
</script>

<style lang="scss" scoped>
.upload { min-width: 280px; }
.item, .empty { border: 1px solid rgba(0, 0, 0, 0.08); background: #fff; overflow: hidden; height: 100%; }
.body--dark .item, .body--dark .empty { border-color: rgba(255, 255, 255, 0.08); background: $dark; }
.empty { border-style: dashed; }
.thumb { width: 100%; aspect-ratio: 4 / 5; object-fit: cover; display: block; background: #05040b; }
</style>

<template>
  <q-dialog v-model="open" @hide="reset">
    <q-card style="width: 720px; max-width: 96vw" class="q-pa-sm">
      <q-card-section class="text-h6">{{ $t('reelImport.title') }}</q-card-section>
      <q-card-section class="q-gutter-md">
        <!-- Atalho: escolhe todos os arquivos do reel de uma vez e cada um vai pro seu campo pelo nome. -->
        <q-file :model-value="null" outlined multiple accept=".txt,.mp4,.mov,.jpg,.jpeg" :label="$t('reelImport.all')"
          :hint="$t('reelImport.allHint')" @update:model-value="pickAll">
          <template #prepend><q-icon :name="mdiFolderMultipleOutline" /></template>
        </q-file>
        <q-file v-model="txt" outlined accept=".txt" :label="$t('reelImport.txt')" @update:model-value="readTxt" />
        <q-banner v-for="e in blocks?.errors ?? []" :key="e" rounded dense class="bg-red-1 text-negative">{{ e }}</q-banner>
        <div class="row q-col-gutter-sm">
          <q-file v-model="igVideo" outlined accept=".mp4,.mov" class="col-12 col-sm-6" :label="$t('reelImport.igVideo')" />
          <q-file v-model="siteVideo" outlined accept=".mp4,.mov" class="col-12 col-sm-6" :label="$t('reelImport.siteVideo')" />
          <q-file v-model="igCover" outlined clearable accept=".jpg,.jpeg" class="col-12 col-sm-6" :label="$t('reelImport.igCover')"
            :hint="$t('reelImport.igCoverHint')" />
        </div>
        <q-input v-model="when" outlined type="datetime-local" stack-label :label="$t('pages.publications.when')" />

        <!-- Conferência: a legenda inteira de cada canal antes de criar (lição do 1º post). -->
        <template v-for="c in previews" :key="c.channel">
          <div class="text-subtitle2">{{ c.label }}</div>
          <q-card flat bordered class="q-pa-sm caption-preview">{{ c.text }}</q-card>
          <div class="text-caption text-grey-7">{{ c.note }}</div>
        </template>
      </q-card-section>
      <q-card-actions align="right">
        <q-btn flat no-caps :label="$t('common.actions.cancel')" v-close-popup />
        <q-btn unelevated color="primary" no-caps :label="$t('reelImport.create')" :loading="working" :disable="!canCreate" @click="create" />
      </q-card-actions>
    </q-card>
  </q-dialog>
</template>

<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import { useQuasar } from 'quasar';
import { isAxiosError } from 'axios';
import { api } from 'src/services/api';
import { mdiFolderMultipleOutline } from '@quasar/extras/mdi-v7';
import { classifyReelFiles, parseReelText, type ReelBlocks } from 'src/utils/reelImport';
import type { Publication } from 'src/types/api';
import { fromLocalInput } from 'src/utils/publications';

defineOptions({ name: 'ReelImportDialog' });

const props = defineProps<{ brandKey: string; defaultWhen: string }>();
const emit = defineEmits<{ done: [] }>();
const open = defineModel<boolean>({ default: false });
const { t } = useI18n();
const $q = useQuasar();

const txt = ref<File | null>(null);
const igVideo = ref<File | null>(null);
const siteVideo = ref<File | null>(null);
const igCover = ref<File | null>(null);
const when = ref(props.defaultWhen);
const blocks = ref<ReelBlocks | null>(null);
const working = ref(false);

// A data sugerida pode mudar depois que a agenda carrega: vale a da hora em que o diálogo abre.
watch(open, (isOpen) => {
  if (isOpen) when.value = props.defaultWhen;
});

function reset() {
  txt.value = igVideo.value = siteVideo.value = igCover.value = null;
  blocks.value = null;
  when.value = props.defaultWhen;
}

async function readTxt(file: File | null) {
  blocks.value = file ? parseReelText(await file.text()) : null;
}

/** Preenche só o que veio na seleção; o que já estava escolhido e não veio fica. */
async function pickAll(files: File[] | File | null) {
  const found = classifyReelFiles(Array.isArray(files) ? files : files ? [files] : []);
  if (found.igVideo) igVideo.value = found.igVideo;
  if (found.siteVideo) siteVideo.value = found.siteVideo;
  if (found.igCover) igCover.value = found.igCover;
  if (found.txt) {
    txt.value = found.txt;
    await readTxt(found.txt);
  }
}

const previews = computed(() => {
  const b = blocks.value;
  if (!b) return [];
  const out: { channel: string; label: string; text: string; note: string }[] = [];
  if (b.instagram) out.push({ channel: 'ig', label: 'Instagram', text: b.instagram, note: t('reelImport.igNote') });
  if (b.linkedin) out.push({ channel: 'li', label: 'LinkedIn', text: b.linkedin, note: t('reelImport.liNote') });
  if (b.facebook) out.push({ channel: 'fb', label: 'Facebook', text: b.facebook, note: t('reelImport.fbNote') });
  return out;
});

const canCreate = computed(() =>
  !!blocks.value && blocks.value.errors.length === 0 && !!when.value &&
  ((!!blocks.value.instagram && !!igVideo.value) ||
    ((!!blocks.value.linkedin || !!blocks.value.facebook) && !!siteVideo.value)));

async function uploadMedia(file: File): Promise<string> {
  const body = new FormData();
  body.append('file', file);
  const { data } = await api.post<{ url: string }>(`/admin/brands/${props.brandKey}/media`, body, { timeout: 15 * 60_000 });
  return data.url;
}

async function createDraft(channel: string, mediaUrl: string, caption: string, scheduledFor: string): Promise<Publication> {
  const { data } = await api.post<Publication>(`/admin/brands/${props.brandKey}/publications`, { channel, mediaUrl, caption, scheduledFor });
  return data;
}

async function create() {
  const b = blocks.value;
  if (!b) return;
  working.value = true;
  try {
    const scheduledFor = fromLocalInput(when.value);
    let created = 0;
    if (b.instagram && igVideo.value) {
      const ig = await createDraft('instagram', await uploadMedia(igVideo.value), b.instagram, scheduledFor);
      // Capa já no rascunho: entra na mesma aprovação, sem precisar editar depois.
      if (igCover.value)
        await api.put(`/admin/brands/${props.brandKey}/publications/${ig.id}/reel`,
          { coverUrl: await uploadMedia(igCover.value), thumbOffsetMs: null, shareToFeed: true });
      created++;
    }
    // O -narrado.mp4 serve LinkedIn e Facebook: sobe uma vez e os dois rascunhos usam o mesmo link.
    const site = siteVideo.value && (b.linkedin || b.facebook) ? await uploadMedia(siteVideo.value) : null;
    if (b.linkedin && site) { await createDraft('linkedin', site, b.linkedin, scheduledFor); created++; }
    if (b.facebook && site) { await createDraft('facebook', site, b.facebook, scheduledFor); created++; }
    $q.notify({ type: 'positive', message: t('reelImport.created', { n: created }) });
    open.value = false;
    emit('done');
  } catch (e) {
    const msg = isAxiosError(e) && typeof e.response?.data?.error === 'string' ? e.response.data.error : t('common.saveError');
    $q.notify({ type: 'negative', message: msg });
  } finally {
    working.value = false;
  }
}
</script>

<style lang="scss" scoped>
.caption-preview { white-space: pre-line; max-height: 180px; overflow-y: auto; font-size: 13px; }
</style>

<template>
  <q-page class="q-pa-lg">
    <div class="row items-end q-mb-lg q-gutter-sm">
      <div class="col">
        <div class="text-h5 sora-bold">{{ $t('pages.publications.title') }}</div>
        <div class="text-grey-7">{{ $t('pages.publications.subtitle') }}</div>
      </div>
      <template v-if="brand.selected">
        <q-btn flat round :icon="mdiChevronLeft" @click="shiftWeek(-1)" />
        <div class="text-subtitle2">{{ weekLabel }}</div>
        <q-btn flat round :icon="mdiChevronRight" @click="shiftWeek(1)" />
        <q-btn flat no-caps :label="$t('pages.publications.today')" @click="goToday" />
        <q-btn outline color="primary" no-caps :icon="mdiMovieOpenOutline" :label="$t('reelImport.title')" @click="importOpen = true" />
        <q-btn unelevated color="primary" no-caps :icon="mdiPlus" :label="$t('pages.publications.new')" @click="openNew" />
      </template>
    </div>

    <q-banner v-if="!brand.selected" rounded class="bg-blue-1 text-primary">{{ $t('pages.publications.pickBrand') }}</q-banner>

    <template v-else>
      <q-banner v-if="!live" rounded class="bg-amber-1 text-brown-9 q-mb-md">
        <template #avatar><q-icon :name="mdiInformationOutline" /></template>
        {{ $t('pages.publications.dryRun') }}
      </q-banner>

      <q-banner v-if="error" rounded class="bg-red-1 text-negative q-mb-md">
        {{ $t('common.error') }}
        <template #action><q-btn flat :label="$t('common.actions.retry')" @click="load" /></template>
      </q-banner>

      <q-linear-progress v-if="loading" indeterminate color="primary" class="q-mb-sm" />

      <div v-for="day in days" :key="day.key" class="q-mb-md">
        <div class="text-subtitle2 q-mb-xs" :class="{ 'text-primary': day.key === todayKey }">
          {{ day.date.toLocaleDateString(locale, { weekday: 'long', day: '2-digit', month: '2-digit' }) }}
        </div>
        <q-list v-if="day.items.length" bordered separator class="card-rounded">
          <q-item v-for="p in day.items" :key="p.id">
            <q-item-section avatar top>
              <div class="text-weight-bold">{{ new Date(p.scheduledFor).toLocaleTimeString(locale, { hour: '2-digit', minute: '2-digit' }) }}</div>
            </q-item-section>
            <q-item-section>
              <q-item-label>
                <q-chip dense square :color="statusColor(p.status)" text-color="white" class="q-ml-none">
                  {{ $t(`pages.publications.status.${p.status}`) }}
                </q-chip>
                <q-chip dense square outline>{{ p.channel }}</q-chip>
              </q-item-label>
              <q-item-label class="q-mt-xs caption-text">{{ p.caption }}</q-item-label>
              <q-item-label caption>
                <a :href="p.mediaUrl" target="_blank" rel="noopener">{{ $t('pages.publications.media') }}</a>
                <span v-if="p.approvedBy"> · {{ $t('pages.publications.approvedBy', { who: p.approvedBy }) }}</span>
                <span v-if="p.publishedUrl"> · <a :href="p.publishedUrl" target="_blank" rel="noopener">{{ $t('pages.publications.openPost') }}</a></span>
              </q-item-label>
              <q-item-label v-if="p.error" caption class="text-negative">{{ p.error }}</q-item-label>
            </q-item-section>
            <q-item-section side top>
              <div class="row no-wrap">
                <template v-if="p.channel === 'LinkedIn' && p.status === 'Approved'">
                  <q-btn flat dense no-caps color="primary" :label="$t('pages.publications.download')" :href="p.mediaUrl" target="_blank" />
                  <q-btn flat dense no-caps color="primary" :label="$t('pages.publications.copyCaption')" @click="copyCaption(p)" />
                  <q-btn flat dense no-caps color="positive" :label="$t('pages.publications.markPublished')" @click="markPublished(p)" />
                </template>
                <q-btn v-if="p.status === 'Draft'" flat dense no-caps color="primary" :label="$t('pages.publications.approve')"
                  :loading="busy === p.id" @click="approve(p)" />
                <q-btn v-if="canEdit(p)" flat round dense :icon="mdiPencilOutline" @click="openEdit(p)">
                  <q-tooltip>{{ $t('pages.publications.edit') }}</q-tooltip>
                </q-btn>
              </div>
            </q-item-section>
          </q-item>
        </q-list>
        <div v-else class="text-caption text-grey-6 q-pl-sm">{{ $t('pages.publications.nothing') }}</div>
      </div>
    </template>

    <!-- Criar ou editar. Editar a legenda de um post aprovado volta ele pra rascunho (aprovação vale pra uma versão). -->
    <q-dialog v-model="dialog">
      <q-card style="width: 560px; max-width: 94vw" class="q-pa-sm">
        <q-card-section class="text-h6">{{ editing ? $t('pages.publications.edit') : $t('pages.publications.new') }}</q-card-section>
        <q-card-section class="q-gutter-md">
          <q-select v-if="!editing" v-model="form.channel" outlined :options="channels" :label="$t('pages.publications.channel')" />
          <template v-if="!editing || canEdit(editing)">
            <q-file v-model="upload" outlined clearable accept=".jpg,.jpeg,.png,.mp4,.mov" :label="$t('pages.publications.uploadFile')"
              :hint="$t('pages.publications.uploadHint')" :loading="uploading" @update:model-value="sendFile">
              <template #prepend><q-icon :name="mdiUpload" /></template>
            </q-file>
            <q-input v-model="form.mediaUrl" outlined dense :label="$t('pages.publications.mediaUrl')" :hint="$t('pages.publications.mediaHint')" />
            <q-expansion-item v-if="isReel" dense dense-toggle switch-toggle-side :label="$t('pages.publications.reelOptions')"
              :caption="$t('pages.publications.reelOptionsHint')" header-class="q-px-none">
              <div class="q-gutter-sm q-pt-sm">
                <q-toggle v-model="form.shareToFeed" :label="$t('pages.publications.shareToFeed')" />
                <q-file v-model="coverUpload" outlined dense clearable accept=".jpg,.jpeg" :label="$t('pages.publications.coverFile')"
                  :hint="$t('pages.publications.coverHint')" :loading="uploading" @update:model-value="sendCover">
                  <template #prepend><q-icon :name="mdiUpload" /></template>
                </q-file>
                <q-input v-model="form.coverUrl" outlined dense clearable :label="$t('pages.publications.coverUrl')" />
                <q-input v-model.number="form.thumbSeconds" outlined dense type="number" min="0" step="0.1" clearable
                  :disable="!!form.coverUrl" :label="$t('pages.publications.thumbSeconds')" :hint="$t('pages.publications.thumbHint')" />
              </div>
            </q-expansion-item>
          </template>
          <q-input v-model="form.caption" outlined type="textarea" autogrow :label="$t('pages.publications.caption')"
            :hint="editing?.status === 'Approved' ? $t('pages.publications.editResets') : undefined" />
          <q-input v-model="form.when" outlined type="datetime-local" stack-label :label="$t('pages.publications.when')" />
        </q-card-section>
        <q-card-actions align="right">
          <q-btn v-if="editing" flat no-caps color="negative" :icon="mdiDeleteOutline" :label="$t('pages.publications.delete')"
            :loading="deleting" class="q-mr-auto" @click="remove" />
          <q-btn flat no-caps :label="$t('common.actions.cancel')" v-close-popup />
          <q-btn unelevated color="primary" no-caps :label="$t('common.actions.save')" :loading="saving"
            :disable="uploading || !form.caption.trim() || !form.when || (!editing && !form.mediaUrl.trim())" @click="save" />
        </q-card-actions>
      </q-card>
    </q-dialog>

    <!-- A checklist bloqueou: mostra o que corrigir. -->
    <q-dialog v-model="blockedDialog">
      <q-card style="width: 560px; max-width: 94vw" class="q-pa-sm">
        <q-card-section class="text-h6 text-negative">{{ $t('pages.publications.blocked') }}</q-card-section>
        <q-list separator>
          <q-item v-for="f in blockedFindings" :key="f.id">
            <q-item-section avatar><q-badge :color="findingColor(f)" rounded /></q-item-section>
            <q-item-section>
              <q-item-label>{{ f.message }}</q-item-label>
              <q-item-label v-if="f.fix" caption>{{ f.fix }}</q-item-label>
            </q-item-section>
          </q-item>
        </q-list>
        <q-card-actions align="right"><q-btn flat no-caps :label="$t('common.actions.close')" v-close-popup /></q-card-actions>
      </q-card>
    </q-dialog>
    <ReelImportDialog v-model="importOpen" :brand-key="brand.selected" :default-when="nextSlot()" @done="load" />
  </q-page>
</template>

<script setup lang="ts">
import { computed, onMounted, reactive, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import { useQuasar } from 'quasar';
import { isAxiosError } from 'axios';
import { mdiChevronLeft, mdiChevronRight, mdiPlus, mdiPencilOutline, mdiInformationOutline, mdiUpload, mdiDeleteOutline, mdiMovieOpenOutline } from '@quasar/extras/mdi-v7';
import { api } from 'src/services/api';
import type { CopyFinding, Publication, SocialChannel } from 'src/types/api';
import { useBrandStore } from 'src/stores/brandStore';
import ReelImportDialog from 'src/components/ReelImportDialog.vue';
import { findingColor, sortFindings } from 'src/utils/brandProfile';
import {
  addDays, canEdit, fromLocalInput, groupByDay, localDayKey, startOfWeek, statusColor, toLocalInput,
} from 'src/utils/publications';

defineOptions({ name: 'PublicationsPage' });

const { t, locale } = useI18n();
const $q = useQuasar();
const brand = useBrandStore();

const weekStart = ref(startOfWeek(new Date()));
const items = ref<Publication[]>([]);
const loading = ref(false);
const saving = ref(false);
const error = ref(false);
const busy = ref<string | null>(null);
const live = ref(false);

const channels: SocialChannel[] = ['Instagram', 'Facebook', 'LinkedIn'];
const todayKey = localDayKey(new Date());
const days = computed(() => groupByDay(weekStart.value, items.value));
const base = computed(() => `/admin/brands/${brand.selected}/publications`);
const weekLabel = computed(() => {
  const end = addDays(weekStart.value, 6);
  const f = (d: Date) => d.toLocaleDateString(locale.value, { day: '2-digit', month: '2-digit' });
  return t('pages.publications.range', { from: f(weekStart.value), to: f(end) });
});

function apiError(e: unknown): string {
  if (isAxiosError(e) && typeof e.response?.data?.error === 'string') return e.response.data.error;
  return t('common.saveError');
}

async function load() {
  if (!brand.selected) return;
  loading.value = true;
  error.value = false;
  try {
    const [list, mode] = await Promise.all([
      api.get<Publication[]>(base.value, {
        params: { from: weekStart.value.toISOString(), to: addDays(weekStart.value, 7).toISOString() },
      }),
      api.get<{ live: boolean }>('/admin/publications/mode'),
    ]);
    items.value = list.data;
    live.value = mode.data.live;
  } catch {
    error.value = true;
  } finally {
    loading.value = false;
  }
}

function shiftWeek(n: number) {
  weekStart.value = addDays(weekStart.value, 7 * n);
  void load();
}

function goToday() {
  weekStart.value = startOfWeek(new Date());
  void load();
}

const dialog = ref(false);
const editing = ref<Publication | null>(null);
interface PublicationForm {
  channel: SocialChannel;
  mediaUrl: string;
  caption: string;
  when: string;
  coverUrl: string | null;
  thumbSeconds: number | null;
  shareToFeed: boolean;
}
const emptyReel = { coverUrl: null, thumbSeconds: null, shareToFeed: true };
const form = reactive<PublicationForm>({ channel: 'Instagram', mediaUrl: '', caption: '', when: '', ...emptyReel });

/** Capa e feed só valem pra Reels: vídeo no Instagram (mesma regra da API, pela extensão). */
const isReel = computed(() => form.channel === 'Instagram' && /\.(mp4|mov)$/i.test(safePath(form.mediaUrl)));

function safePath(url: string): string {
  try {
    return new URL(url.trim()).pathname;
  } catch {
    return '';
  }
}

const upload = ref<File | null>(null);
const coverUpload = ref<File | null>(null);
const uploading = ref(false);

/** Sobe o arquivo pro bucket e devolve o link público estável que a API gera. */
async function uploadToBucket(file: File, reset: () => void): Promise<string | null> {
  uploading.value = true;
  try {
    const body = new FormData();
    body.append('file', file);
    const { data } = await api.post<{ url: string }>(`/admin/brands/${brand.selected}/media`, body, { timeout: 15 * 60_000 });
    $q.notify({ type: 'positive', message: t('pages.publications.uploaded') });
    return data.url;
  } catch (e) {
    reset();
    $q.notify({ type: 'negative', message: apiError(e) });
    return null;
  } finally {
    uploading.value = false;
  }
}

async function sendFile(file: File | null) {
  if (!file) return;
  const url = await uploadToBucket(file, () => (upload.value = null));
  if (url) form.mediaUrl = url;
}

async function sendCover(file: File | null) {
  if (!file) return;
  const url = await uploadToBucket(file, () => (coverUpload.value = null));
  if (url) form.coverUrl = url;
}

/** O que vai pro PUT /reel. Quadro em segundos no formulário, milissegundos na API. */
function reelBody() {
  const cover = form.coverUrl?.trim() || null;
  const secs = typeof form.thumbSeconds === 'number' && Number.isFinite(form.thumbSeconds) ? form.thumbSeconds : null;
  return { coverUrl: cover, thumbOffsetMs: cover || secs === null ? null : Math.round(secs * 1000), shareToFeed: form.shareToFeed };
}

const importOpen = ref(false);

/** Próxima hora cheia, no formato do campo de data e hora. */
function nextSlot(): string {
  const next = new Date();
  next.setHours(next.getHours() + 1, 0, 0, 0);
  return toLocalInput(next.toISOString());
}

async function copyCaption(p: Publication) {
  try {
    await navigator.clipboard.writeText(p.caption);
    $q.notify({ type: 'positive', message: t('pages.publications.copied') });
  } catch {
    $q.notify({ type: 'negative', message: t('common.saveError') });
  }
}

/** LinkedIn é manual: depois de postar lá, registra aqui com o link do post (opcional). */
function markPublished(p: Publication) {
  $q.dialog({
    title: t('pages.publications.markPublished'),
    message: t('pages.publications.markPublishedHint'),
    prompt: { model: '', type: 'url' },
    cancel: true,
  }).onOk((url: string) => {
    void api.post(`${base.value}/${p.id}/mark-published`, { url: url?.trim() || null })
      .then(() => load())
      .catch((e: unknown) => $q.notify({ type: 'negative', message: apiError(e) }));
  });
}

function openNew() {
  upload.value = null;
  editing.value = null;
  const next = new Date();
  next.setHours(next.getHours() + 1, 0, 0, 0);
  coverUpload.value = null;
  Object.assign(form, { channel: 'Instagram', mediaUrl: '', caption: '', when: toLocalInput(next.toISOString()), ...emptyReel });
  dialog.value = true;
}

function openEdit(p: Publication) {
  editing.value = p;
  upload.value = null;
  coverUpload.value = null;
  Object.assign(form, {
    channel: p.channel, mediaUrl: p.mediaUrl, caption: p.caption, when: toLocalInput(p.scheduledFor),
    coverUrl: p.coverUrl, thumbSeconds: p.thumbOffsetMs === null ? null : p.thumbOffsetMs / 1000, shareToFeed: p.shareToFeed,
  });
  dialog.value = true;
}

const deleting = ref(false);

function remove() {
  const p = editing.value;
  if (!p) return;
  $q.dialog({ title: t('pages.publications.delete'), message: t('pages.publications.deleteConfirm'), cancel: true, persistent: true })
    .onOk(() => void doRemove(p));
}

async function doRemove(p: Publication) {
  deleting.value = true;
  try {
    await api.delete(`${base.value}/${p.id}`);
    dialog.value = false;
    await load();
  } catch (e) {
    $q.notify({ type: 'negative', message: apiError(e) });
  } finally {
    deleting.value = false;
  }
}

async function save() {
  saving.value = true;
  try {
    const when = fromLocalInput(form.when);
    const p = editing.value;
    if (!p) {
      const { data } = await api.post<Publication>(base.value, { channel: form.channel, mediaUrl: form.mediaUrl.trim(), caption: form.caption, scheduledFor: when });
      const reel = reelBody();
      if (isReel.value && (reel.coverUrl || reel.thumbOffsetMs !== null || !reel.shareToFeed))
        await api.put(`${base.value}/${data.id}/reel`, reel);
    } else {
      // Trocar vídeo ou legenda volta o post pra rascunho (precisa aprovar de novo).
      if (form.mediaUrl.trim() !== p.mediaUrl) await api.put(`${base.value}/${p.id}/media`, { mediaUrl: form.mediaUrl.trim() });
      if (form.caption.trim() !== p.caption) await api.put(`${base.value}/${p.id}/caption`, { caption: form.caption });
      const reel = reelBody();
      if (isReel.value && (reel.coverUrl !== p.coverUrl || reel.thumbOffsetMs !== p.thumbOffsetMs || reel.shareToFeed !== p.shareToFeed))
        await api.put(`${base.value}/${p.id}/reel`, reel);
      if (when !== new Date(p.scheduledFor).toISOString()) await api.put(`${base.value}/${p.id}/schedule`, { scheduledFor: when });
    }
    dialog.value = false;
    await load();
  } catch (e) {
    $q.notify({ type: 'negative', message: apiError(e) });
  } finally {
    saving.value = false;
  }
}

const blockedDialog = ref(false);
const blockedFindings = ref<CopyFinding[]>([]);

async function approve(p: Publication) {
  busy.value = p.id;
  try {
    await api.post(`${base.value}/${p.id}/approve`);
    $q.notify({ type: 'positive', message: t('pages.publications.approved') });
    await load();
  } catch (e) {
    const checklist = isAxiosError(e) && e.response?.status === 422 ? (e.response.data?.checklist as CopyFinding[] | undefined) : undefined;
    if (checklist) {
      blockedFindings.value = sortFindings(checklist).filter((f) => f.status === 'Fail');
      blockedDialog.value = true;
    } else {
      $q.notify({ type: 'negative', message: apiError(e) });
    }
  } finally {
    busy.value = null;
  }
}

onMounted(load);
watch(() => brand.selected, () => void load());
</script>

<style lang="scss" scoped>
.caption-text { white-space: pre-line; }
</style>

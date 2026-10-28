<template>
  <q-page class="q-pa-lg">
    <div class="row items-center q-mb-lg q-gutter-sm">
      <div class="col-12 col-md">
        <div class="text-overline text-grey-7">{{ brand.selected }}</div>
        <div class="text-h5 sora-bold">{{ heading }}</div>
      </div>
      <template v-if="brand.selected">
        <!-- Fila é o padrão: responde "o que precisa de mim". Semana e mês mostram a mesma agenda no calendário. -->
        <q-btn-toggle v-model="view" no-caps unelevated rounded toggle-color="primary" class="view-toggle" :options="viewOptions" />
        <template v-if="view !== 'queue'">
          <q-btn flat round :icon="mdiChevronLeft" @click="shift(-1)" />
          <q-btn flat no-caps :label="$t('pages.publications.today')" @click="goToday" />
          <q-btn flat round :icon="mdiChevronRight" @click="shift(1)" />
        </template>
        <q-btn unelevated color="accent" text-color="dark" no-caps :icon="mdiMovieOpenOutline" :label="$t('reelImport.title')" @click="importOpen = true" />
        <q-btn outline color="primary" no-caps :icon="mdiPlus" :label="$t('pages.publications.new')" @click="openNew" />
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

      <div class="row q-col-gutter-lg">
        <div class="col-12 col-md">
          <div v-if="loading && !items.length" class="column q-gutter-sm">
            <q-skeleton v-for="n in 4" :key="n" height="76px" class="card-rounded" />
          </div>

          <!-- Fila -->
          <div v-else-if="view === 'queue'" class="column q-gutter-y-lg">
            <div v-if="!sections.needs.length" class="all-clear card-rounded row items-center no-wrap q-pa-md q-gutter-x-md">
              <q-icon :name="mdiCheckCircleOutline" size="32px" color="positive" />
              <div>
                <div class="text-subtitle1 text-weight-bold">{{ $t('pages.publications.allClear') }}</div>
                <div v-if="sections.scheduled[0]" class="text-caption text-grey-7">
                  {{ $t('pages.publications.nextUp', { when: fmtWhen(sections.scheduled[0].scheduledFor) }) }}
                </div>
              </div>
            </div>
            <template v-for="stage in stages" :key="stage">
              <section v-if="sections[stage].length">
                <div class="section-title q-mb-sm" :class="`text-${stageColor(stage)}`">
                  {{ $t(`pages.publications.sections.${stage}`) }} · {{ sections[stage].length }}
                </div>
                <transition-group name="list" tag="div" class="column q-gutter-y-sm">
                  <div v-for="b in sections[stage]" :key="b.key" class="bundle-row card-rounded row items-center no-wrap q-pa-sm q-gutter-x-md"
                    :class="{ selected: b.key === selectedKey }" role="button" tabindex="0" @click="selectBundle(b, true)" @keydown.enter="selectBundle(b, true)">
                    <div class="thumb" :style="thumbStyle(b)"><q-icon v-if="!thumbOf(b)" :name="mdiPlayCircleOutline" color="white" size="20px" /></div>
                    <div class="col" style="min-width: 0">
                      <div class="text-weight-bold ellipsis">{{ b.title }}</div>
                      <div class="text-caption text-grey-7">{{ fmtWhen(b.scheduledFor) }}</div>
                      <div class="row q-gutter-xs q-mt-xs">
                        <span v-for="p in b.items" :key="p.id" class="channel-chip" :class="`ch-${p.channel}`">
                          <span class="dot" :class="`bg-${statusColor(p.status)}`" />{{ p.channel }}
                        </span>
                      </div>
                    </div>
                    <q-chip dense :color="stageColor(stage)" text-color="white" class="gt-xs">{{ $t(`pages.publications.stage.${stage}`) }}</q-chip>
                  </div>
                </transition-group>
              </section>
            </template>
            <div v-if="!bundles.length" class="text-grey-6">{{ $t('pages.publications.nothing') }}</div>
          </div>

          <!-- Semana -->
          <div v-else-if="view === 'week'" class="week-scroll">
            <div class="week-grid">
              <div v-for="day in weekDays" :key="day.key" class="day-col column q-gutter-y-sm" :class="dropClass(day.key)"
                @dragover="onDragOver($event, day.key)" @dragleave="dropTarget = null" @drop="onDrop(day.key)">
                <div class="day-head card-rounded column q-py-xs" :class="{ today: day.key === todayKey }">
                  <span class="text-caption text-uppercase">{{ day.date.toLocaleDateString(locale, { weekday: 'short' }) }}</span>
                  <span class="sora-bold text-h6">{{ day.date.getDate() }}</span>
                </div>
                <div v-for="b in day.bundles" :key="b.key" class="week-card card-rounded q-pa-sm"
                  :class="{ selected: b.key === selectedKey, movable: canMove(b) }" :draggable="canMove(b)"
                  @dragstart="onDragStart($event, b)" @dragend="onDragEnd"
                  role="button" tabindex="0" @click="selectBundle(b, true)" @keydown.enter="selectBundle(b, true)">
                  <div class="text-caption text-weight-bold">{{ fmtTime(b.scheduledFor) }}</div>
                  <div class="text-body2 text-weight-medium ellipsis-2-lines q-my-xs">{{ b.title }}</div>
                  <div class="row q-gutter-xs">
                    <span v-for="p in b.items" :key="p.id" class="channel-chip" :class="`ch-${p.channel}`">
                      <span class="dot" :class="`bg-${statusColor(p.status)}`" />{{ p.channel.slice(0, 2) }}
                    </span>
                  </div>
                </div>
                <div v-if="!day.bundles.length" class="empty-day card-rounded text-caption text-grey-6 q-pa-sm text-center">
                  {{ $t('pages.publications.nothing') }}
                </div>
              </div>
            </div>
          </div>

          <!-- Mês -->
          <div v-else class="month-grid">
            <div v-for="c in monthCells.slice(0, 7)" :key="`wd${c.key}`" class="text-caption text-uppercase text-grey-7 q-px-xs">
              {{ c.date.toLocaleDateString(locale, { weekday: 'short' }) }}
            </div>
            <div v-for="c in monthCells" :key="c.key" class="month-cell card-rounded" :class="[{ out: !c.inMonth }, dropClass(c.key)]"
              @dragover="onDragOver($event, c.key)" @dragleave="dropTarget = null" @drop="onDrop(c.key)">
              <span class="day-num" :class="{ today: c.key === todayKey }">{{ c.date.getDate() }}</span>
              <div v-for="b in c.bundles" :key="b.key" class="month-chip" :class="[`st-${b.stage}`, { selected: b.key === selectedKey, movable: canMove(b) }]"
                :draggable="canMove(b)" @dragstart="onDragStart($event, b)" @dragend="onDragEnd"
                role="button" tabindex="0" @click="selectBundle(b, true)" @keydown.enter="selectBundle(b, true)">
                <span class="text-weight-bold">{{ fmtTime(b.scheduledFor) }}</span> {{ b.title }}
              </div>
            </div>
          </div>
        </div>

        <div class="col-12 col-md-auto">
          <div ref="previewEl" class="preview-col">
            <PublicationPreview :bundle="selectedBundle" :selected-id="selectedId" :brand-key="brand.selected" :busy="approving"
              @select="(id: string) => (selectedId = id)" @approve="approveBundle" @edit="openEdit" @copy="copyCaption"
              @mark-published="markPublished" />
          </div>
        </div>
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
import { computed, onBeforeUnmount, onMounted, reactive, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import { useQuasar } from 'quasar';
import { useRoute, useRouter } from 'vue-router';
import { isAxiosError } from 'axios';
import {
  mdiChevronLeft, mdiChevronRight, mdiPlus, mdiInformationOutline, mdiUpload, mdiDeleteOutline, mdiMovieOpenOutline,
  mdiCheckCircleOutline, mdiPlayCircleOutline,
} from '@quasar/extras/mdi-v7';
import { api } from 'src/services/api';
import type { CopyFinding, Publication, SocialChannel } from 'src/types/api';
import { useBrandStore } from 'src/stores/brandStore';
import ReelImportDialog from 'src/components/ReelImportDialog.vue';
import PublicationPreview from 'src/components/PublicationPreview.vue';
import { findingColor, sortFindings } from 'src/utils/brandProfile';
import {
  addDays, bundlePosts, canEdit, fromLocalInput, groupByDay, isVideo, localDayKey, monthGrid, queueSections, stageColor,
  startOfWeek, statusColor, toLocalInput, canMove, moveToDay, type AgendaView, type BundleStage, type PostBundle,
} from 'src/utils/publications';

defineOptions({ name: 'PublicationsPage' });

const { t, locale } = useI18n();
const $q = useQuasar();
const brand = useBrandStore();
const route = useRoute();
const router = useRouter();

const VIEW_KEY = 'rb.publications.view';
function savedView(): AgendaView {
  try {
    const v = localStorage.getItem(VIEW_KEY);
    return v === 'week' || v === 'month' ? v : 'queue';
  } catch {
    return 'queue';
  }
}

const view = ref<AgendaView>(savedView());
const viewOptions = computed(() => [
  { label: t('pages.publications.views.queue'), value: 'queue' },
  { label: t('pages.publications.views.week'), value: 'week' },
  { label: t('pages.publications.views.month'), value: 'month' },
]);
const weekStart = ref(startOfWeek(new Date()));
const monthAnchor = ref(firstOfMonth(new Date()));
const items = ref<Publication[]>([]);
const loading = ref(false);
const saving = ref(false);
const error = ref(false);
const approving = ref(false);
const live = ref(false);

const channels: SocialChannel[] = ['Instagram', 'Facebook', 'LinkedIn'];
const stages: BundleStage[] = ['needs', 'scheduled', 'published'];
const todayKey = localDayKey(new Date());
const base = computed(() => `/admin/brands/${brand.selected}/publications`);

function firstOfMonth(d: Date): Date {
  return new Date(d.getFullYear(), d.getMonth(), 1);
}

const bundles = computed(() => bundlePosts(items.value));
const sections = computed(() => queueSections(bundles.value));
const weekDays = computed(() => groupByDay(weekStart.value, items.value).map((d) => ({ ...d, bundles: bundlePosts(d.items) })));
const monthCells = computed(() => {
  const byDay = new Map<string, PostBundle[]>();
  for (const b of bundles.value) {
    const k = localDayKey(new Date(b.scheduledFor));
    byDay.set(k, [...(byDay.get(k) ?? []), b]);
  }
  return monthGrid(monthAnchor.value).map((date) => {
    const key = localDayKey(date);
    return { key, date, inMonth: date.getMonth() === monthAnchor.value.getMonth(), bundles: byDay.get(key) ?? [] };
  });
});

/** Ordem que J e K percorrem: a mesma que aparece na tela. */
const ordered = computed<PostBundle[]>(() =>
  view.value === 'queue' ? [...sections.value.needs, ...sections.value.scheduled, ...sections.value.published] : bundles.value,
);

const heading = computed(() => {
  if (view.value === 'queue') return t('pages.publications.queueTitle');
  if (view.value === 'month') {
    const m = monthAnchor.value.toLocaleDateString(locale.value, { month: 'long', year: 'numeric' });
    return m.charAt(0).toUpperCase() + m.slice(1);
  }
  const f = (d: Date) => d.toLocaleDateString(locale.value, { day: '2-digit', month: '2-digit' });
  return t('pages.publications.range', { from: f(weekStart.value), to: f(addDays(weekStart.value, 6)) });
});

function fmtWhen(iso: string): string {
  return new Date(iso).toLocaleString(locale.value, { weekday: 'short', day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit' });
}
function fmtTime(iso: string): string {
  return new Date(iso).toLocaleTimeString(locale.value, { hour: '2-digit', minute: '2-digit' });
}

/** Miniatura: capa do Reels ou a própria imagem. Vídeo sem capa fica no bloco de cor (não baixa vídeo só pra miniatura). */
function thumbOf(b: PostBundle): string | null {
  for (const p of b.items) {
    if (p.coverUrl) return p.coverUrl;
    if (!isVideo(p.mediaUrl)) return p.mediaUrl;
  }
  return null;
}
function thumbStyle(b: PostBundle) {
  const url = thumbOf(b);
  return url ? { backgroundImage: `url("${url}")` } : {};
}

function apiError(e: unknown): string {
  if (isAxiosError(e) && typeof e.response?.data?.error === 'string') return e.response.data.error;
  return t('common.saveError');
}

/** Intervalo de cada visão. Fila: três semanas pra trás (publicados recentes) e quatro meses pra frente. */
function range(): { from: Date; to: Date } {
  if (view.value === 'week') return { from: weekStart.value, to: addDays(weekStart.value, 7) };
  if (view.value === 'month') {
    const grid = monthGrid(monthAnchor.value);
    return { from: grid[0]!, to: addDays(grid[grid.length - 1]!, 1) };
  }
  const now = new Date();
  return { from: addDays(now, -21), to: addDays(now, 120) };
}

async function load() {
  if (!brand.selected) return;
  loading.value = true;
  error.value = false;
  try {
    const { from, to } = range();
    const [list, mode] = await Promise.all([
      api.get<Publication[]>(base.value, { params: { from: from.toISOString(), to: to.toISOString() } }),
      api.get<{ live: boolean }>('/admin/publications/mode'),
    ]);
    items.value = list.data;
    live.value = mode.data.live;
    keepSelection();
  } catch {
    error.value = true;
  } finally {
    loading.value = false;
  }
}

const selectedKey = ref<string | null>(null);
const selectedId = ref<string | null>(null);
const selectedBundle = computed(() => bundles.value.find((b) => b.key === selectedKey.value) ?? null);

const previewEl = ref<HTMLElement | null>(null);

function selectBundle(b: PostBundle | undefined, reveal = false) {
  if (!b) return;
  selectedKey.value = b.key;
  selectedId.value = (b.items.find((p) => p.status === 'Draft' || p.status === 'Failed') ?? b.items[0])?.id ?? null;
  // No celular a prévia fica embaixo da lista: tocar no post leva até ela.
  if (reveal && $q.screen.lt.md) previewEl.value?.scrollIntoView({ behavior: 'smooth', block: 'start' });
}

/** Depois de recarregar, continua no mesmo post; se ele sumiu, vai pro primeiro que precisa de você. */
function keepSelection() {
  const still = bundles.value.find((b) => b.key === selectedKey.value);
  if (still) {
    if (!still.items.some((p) => p.id === selectedId.value)) selectedId.value = still.items[0]?.id ?? null;
    return;
  }
  selectBundle(ordered.value[0]);
}

function shift(n: number) {
  if (view.value === 'week') weekStart.value = addDays(weekStart.value, 7 * n);
  else monthAnchor.value = new Date(monthAnchor.value.getFullYear(), monthAnchor.value.getMonth() + n, 1);
  void load();
}

function goToday() {
  weekStart.value = startOfWeek(new Date());
  monthAnchor.value = firstOfMonth(new Date());
  void load();
}

/**
 * Arrastar um grupo pra outro dia (semana e mês): mesmo horário, canais juntos. A tela muda na hora e o servidor
 * confirma depois; se falhar, volta. Dia que já passou não aceita. Aprovação continua valendo (só a data mudou).
 */
const dragging = ref<PostBundle | null>(null);
const dropTarget = ref<string | null>(null);

function onDragStart(e: DragEvent, b: PostBundle) {
  dragging.value = b;
  e.dataTransfer?.setData('text/plain', b.key);
  if (e.dataTransfer) e.dataTransfer.effectAllowed = 'move';
}

function onDragEnd() {
  dragging.value = null;
  dropTarget.value = null;
}

function onDragOver(e: DragEvent, dayKey: string) {
  if (!dragging.value || dayKey < todayKey) return;
  e.preventDefault();
  dropTarget.value = dayKey;
}

function dropClass(dayKey: string) {
  if (!dragging.value) return {};
  return { 'drop-ok': dropTarget.value === dayKey, 'drop-no': dayKey < todayKey };
}

async function onDrop(dayKey: string) {
  const b = dragging.value;
  onDragEnd();
  if (!b || dayKey < todayKey || localDayKey(new Date(b.scheduledFor)) === dayKey) return;
  const fromKey = localDayKey(new Date(b.scheduledFor));
  if (await moveBundle(b, dayKey)) {
    const day = new Date(moveToDay(b.scheduledFor, dayKey)).toLocaleDateString(locale.value, { weekday: 'long', day: '2-digit', month: '2-digit' });
    $q.notify({
      type: 'positive',
      message: t('pages.publications.moved', { day }),
      timeout: 6000,
      actions: [{ label: t('pages.publications.undo'), color: 'white', noCaps: true, handler: () => void moveBundle({ ...b, scheduledFor: moveToDay(b.scheduledFor, dayKey) }, fromKey) }],
    });
  }
}

async function moveBundle(b: PostBundle, dayKey: string): Promise<boolean> {
  const before = items.value;
  const ids = new Set(b.items.map((p) => p.id));
  items.value = items.value.map((p) => (ids.has(p.id) ? { ...p, scheduledFor: moveToDay(p.scheduledFor, dayKey) } : p));
  selectedKey.value = moveToDay(b.scheduledFor, dayKey).slice(0, 16);
  try {
    for (const p of b.items) await api.put(`${base.value}/${p.id}/schedule`, { scheduledFor: moveToDay(p.scheduledFor, dayKey) });
    return true;
  } catch (e) {
    items.value = before;
    $q.notify({ type: 'negative', message: apiError(e) });
    return false;
  } finally {
    await load();
  }
}

watch(view, (v) => {
  try {
    localStorage.setItem(VIEW_KEY, v);
  } catch {
    // Sem armazenamento (aba anônima): só não lembra a visão.
  }
  void load();
});

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

/**
 * Aprova os rascunhos do grupo, um canal por vez (cada um passa pela checklist). Parou na checklist: mostra o que
 * corrigir e mantém aprovado o que já passou. No fim, aviso com "Desfazer", que volta tudo pra rascunho.
 */
async function approveBundle(b: PostBundle) {
  const drafts = b.items.filter((p) => p.status === 'Draft');
  if (!drafts.length || approving.value) return;
  approving.value = true;
  const done: string[] = [];
  try {
    for (const p of drafts) {
      await api.post(`${base.value}/${p.id}/approve`);
      done.push(p.id);
    }
  } catch (e) {
    const checklist = isAxiosError(e) && e.response?.status === 422 ? (e.response.data?.checklist as CopyFinding[] | undefined) : undefined;
    if (checklist) {
      blockedFindings.value = sortFindings(checklist).filter((f) => f.status === 'Fail');
      blockedDialog.value = true;
    } else {
      $q.notify({ type: 'negative', message: apiError(e) });
    }
  } finally {
    approving.value = false;
  }
  if (done.length) {
    $q.notify({
      type: 'positive',
      message: t('pages.publications.approvedN', { n: done.length }, done.length),
      timeout: 6000,
      actions: [{ label: t('pages.publications.undo'), color: 'white', noCaps: true, handler: () => void undo(done) }],
    });
  }
  await load();
}

async function undo(ids: string[]) {
  try {
    for (const id of ids) await api.post(`${base.value}/${id}/unapprove`);
    $q.notify({ type: 'info', message: t('pages.publications.undone') });
  } catch (e) {
    $q.notify({ type: 'negative', message: apiError(e) });
  }
  await load();
}

/** Atalhos: J e K andam na lista, A aprova o grupo, E edita o canal aberto. Fora de campo de texto e de diálogo. */
function onKey(e: KeyboardEvent) {
  if (e.ctrlKey || e.metaKey || e.altKey || dialog.value || blockedDialog.value || importOpen.value) return;
  const el = e.target as HTMLElement | null;
  if (el && (el.isContentEditable || ['INPUT', 'TEXTAREA', 'SELECT'].includes(el.tagName))) return;
  const key = e.key.toLowerCase();
  if (key === 'j' || key === 'k') {
    const list = ordered.value;
    if (!list.length) return;
    const i = list.findIndex((x) => x.key === selectedKey.value);
    const next = key === 'j' ? Math.min(i + 1, list.length - 1) : Math.max(i - 1, 0);
    selectBundle(list[i < 0 ? 0 : next]);
  } else if (key === 'a' && selectedBundle.value) {
    void approveBundle(selectedBundle.value);
  } else if (key === 'e') {
    const p = selectedBundle.value?.items.find((x) => x.id === selectedId.value);
    if (p && canEdit(p)) openEdit(p);
  } else {
    return;
  }
  e.preventDefault();
}

/**
 * Pedidos vindos da busca rápida (Ctrl+K): ?action=new|import abre o diálogo, ?post=<grupo> abre a fila nele.
 * A query é limpa antes, pra não reabrir ao voltar na página.
 */
async function handleQuery() {
  const { action, post } = route.query;
  if (!action && !post) return;
  // Limpa antes de abrir: o Quasar fecha diálogo quando a rota muda.
  await router.replace({ query: {} });
  if (action === 'new') openNew();
  else if (action === 'import') importOpen.value = true;
  if (typeof post === 'string') {
    view.value = 'queue';
    await load();
    selectBundle(bundles.value.find((b) => b.key === post), true);
  }
}

watch(() => route.query, () => void handleQuery());

onMounted(async () => {
  await load();
  await handleQuery();
  window.addEventListener('keydown', onKey);
});
onBeforeUnmount(() => window.removeEventListener('keydown', onKey));
watch(() => brand.selected, () => void load());
</script>

<style lang="scss" scoped>
$line-light: rgba(0, 0, 0, 0.08);
$line-dark: rgba(255, 255, 255, 0.08);

.view-toggle { border: 1px solid $line-light; }
.body--dark .view-toggle { border-color: $line-dark; }
.section-title { font-size: 12px; font-weight: 600; letter-spacing: 0.08em; text-transform: uppercase; }

.bundle-row, .week-card, .month-cell, .all-clear, .day-head, .empty-day {
  border: 1px solid $line-light; background: #fff;
}
.body--dark .bundle-row, .body--dark .week-card, .body--dark .month-cell, .body--dark .all-clear,
.body--dark .day-head, .body--dark .empty-day { border-color: $line-dark; background: $dark; }

.bundle-row, .week-card {
  cursor: pointer; outline: none;
  transition: transform 0.15s ease, border-color 0.15s ease, box-shadow 0.15s ease;
  &:hover { transform: translateX(2px); border-color: rgba($primary, 0.5); }
  &:focus-visible, &.selected { border-color: $primary; box-shadow: 0 0 0 3px rgba($primary, 0.18); }
}
.week-card:hover { transform: translateY(-2px); }
.bundle-row.selected { background: $light-focus; }
.body--dark .bundle-row.selected { background: $dark-focus; }

.thumb {
  width: 40px; height: 64px; border-radius: 10px; flex: none; display: flex; align-items: center; justify-content: center;
  background: linear-gradient(160deg, $primary, $dark-focus) center / cover no-repeat;
}

.channel-chip {
  display: inline-flex; align-items: center; gap: 4px; font-size: 11px; font-weight: 600; padding: 2px 7px; border-radius: 6px;
  &.ch-Instagram { background: #fce7f3; color: #9d174d; }
  &.ch-Facebook { background: #dbeafe; color: #1e40af; }
  &.ch-LinkedIn { background: #cffafe; color: #155e75; }
}
.body--dark .channel-chip {
  &.ch-Instagram { background: #3b1b2e; color: #fbcfe8; }
  &.ch-Facebook { background: #172a4a; color: #bfdbfe; }
  &.ch-LinkedIn { background: #123039; color: #cffafe; }
}
.dot { width: 7px; height: 7px; border-radius: 50%; display: inline-block; }

.week-scroll { overflow-x: auto; }
.week-grid { display: grid; grid-template-columns: repeat(7, minmax(0, 1fr)); gap: 8px; > div { min-width: 0; } }
@media (max-width: $breakpoint-sm-max) { .week-grid { grid-template-columns: repeat(7, minmax(120px, 1fr)); min-width: 860px; } }
.day-head { padding-left: 10px; padding-right: 10px; line-height: 1.2; }
.day-head.today { background: $primary; color: #fff; border-color: $primary; }
.empty-day { border-style: dashed; background: transparent !important; overflow-wrap: anywhere; }

.month-grid { display: grid; grid-template-columns: repeat(7, minmax(0, 1fr)); gap: 6px; }
.month-cell {
  min-height: 96px; padding: 6px; display: flex; flex-direction: column; gap: 4px; border-radius: 12px; min-width: 0;
  &.out { background: transparent !important; .day-num { opacity: 0.45; } }
}
.day-num { font-size: 12px; font-weight: 600; align-self: flex-start; padding: 1px 7px; border-radius: 999px; }
.day-num.today { background: $primary; color: #fff; }
.month-chip {
  font-size: 11px; font-weight: 500; line-height: 1.3; padding: 3px 6px; border-radius: 6px; cursor: pointer; outline: none;
  display: -webkit-box; -webkit-line-clamp: 2; -webkit-box-orient: vertical; overflow: hidden; overflow-wrap: anywhere;
  &.st-needs { background: #fef3c7; color: #92400e; }
  &.st-scheduled { background: #ede7ff; color: #5b2fc2; }
  &.st-published { background: #d1fae5; color: #065f46; }
  &.selected, &:focus-visible { box-shadow: 0 0 0 2px $primary; }
}
.body--dark .month-chip {
  &.st-needs { background: #3d2a12; color: #fcd34d; }
  &.st-scheduled { background: #2b2150; color: #cfc2ff; }
  &.st-published { background: #0f2a20; color: #6ee7b7; }
}

.week-card.movable, .month-chip.movable { cursor: grab; }
.week-card.movable:active, .month-chip.movable:active { cursor: grabbing; }
.day-col, .month-cell { transition: background 0.15s ease, box-shadow 0.15s ease; border-radius: 12px; }
.drop-ok { box-shadow: 0 0 0 2px $primary inset; background: rgba($primary, 0.08) !important; }
.drop-no { opacity: 0.5; }

.preview-col { width: 340px; max-width: 100%; position: sticky; top: 16px; }
@media (max-width: $breakpoint-sm-max) { .preview-col { width: 100%; position: static; } }

.list-move, .list-enter-active, .list-leave-active { transition: all 0.25s ease; }
.list-enter-from, .list-leave-to { opacity: 0; transform: translateX(-12px); }
.list-leave-active { position: absolute; }
</style>
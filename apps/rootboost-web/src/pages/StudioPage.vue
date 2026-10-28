<template>
  <q-page class="q-pa-lg">
    <div class="row items-center q-mb-lg q-gutter-sm">
      <div class="col-12 col-md">
        <div class="page-eyebrow">{{ brand.selectedName }}</div>
        <div class="text-h5 sora-bold">{{ $t('studio.title') }}</div>
        <div class="text-grey-7">{{ $t('studio.subtitle') }}</div>
      </div>
      <q-btn v-if="brand.selected" unelevated color="primary" no-caps :icon="mdiPlus" :label="$t('studio.new')" @click="newOpen = true" />
    </div>

    <q-banner v-if="!brand.selected" rounded class="bg-blue-1 text-primary">{{ $t('studio.pickBrand') }}</q-banner>

    <div v-else class="row q-col-gutter-lg">
      <!-- Lista de jobs -->
      <div class="col-12 col-md-4">
        <div v-if="loading && !jobs.length" class="column q-gutter-sm">
          <q-skeleton v-for="n in 3" :key="n" height="72px" class="card-rounded" />
        </div>
        <div v-else-if="!jobs.length" class="empty card-rounded q-pa-lg text-center text-grey-7">
          <q-icon :name="mdiMovieOpenPlayOutline" size="40px" class="q-mb-sm" />
          <div>{{ $t('studio.empty') }}</div>
        </div>
        <div v-else class="column no-wrap q-gutter-y-sm">
          <div v-for="j in jobs" :key="j.id" class="job-row card-rounded q-pa-sm" :class="{ selected: j.id === selectedId }"
            role="button" tabindex="0" @click="select(j)" @keydown.enter="select(j)">
            <div class="row items-center no-wrap q-gutter-x-sm">
              <div class="col" style="min-width: 0">
                <div class="text-weight-bold ellipsis">{{ j.theme }}</div>
                <div class="text-caption text-grey-7">{{ $t(`studio.kind.${j.kind}`) }} · {{ fmt(j.updatedAt) }}</div>
              </div>
              <q-chip dense :color="jobColor(j.status)" text-color="white">{{ $t(`studio.status.${j.status}`) }}</q-chip>
            </div>
            <q-linear-progress v-if="j.status === 'Rendering'" :value="j.progress / 100" color="primary" rounded class="q-mt-xs" />
          </div>
        </div>
      </div>

      <!-- Detalhe -->
      <div class="col-12 col-md-8">
        <div v-if="!job" class="empty card-rounded q-pa-xl text-center text-grey-6">{{ $t('studio.pick') }}</div>
        <div v-else class="detail card-rounded q-pa-lg column no-wrap q-gutter-y-md">
          <div class="row items-start q-gutter-sm">
            <div class="col">
              <div class="text-h6 sora-bold">{{ job.theme }}</div>
              <div class="text-caption text-grey-7">
                {{ $t(`studio.kind.${job.kind}`) }}<span v-if="job.voiceKey"> · {{ $t('studio.voice') }}: {{ job.voiceKey }}</span>
                <span v-if="job.seed !== null"> · {{ $t('studio.seed') }} {{ job.seed }}</span>
              </div>
            </div>
            <q-chip :color="jobColor(job.status)" text-color="white">{{ $t(`studio.status.${job.status}`) }}</q-chip>
          </div>

          <!-- Etapas -->
          <div class="steps row no-wrap">
            <div v-for="(s, i) in steps" :key="s" class="step col text-center" :class="{ done: i < step, current: i === step }">
              <div class="step-dot" />
              <div class="text-caption">{{ $t(`studio.steps.${s}`) }}</div>
            </div>
          </div>

          <q-banner v-if="job.error" rounded class="bg-red-1 text-negative">{{ job.error }}</q-banner>

          <!-- Render em andamento -->
          <div v-if="job.status === 'Queued' || job.status === 'Rendering'" class="column q-gutter-y-xs">
            <div class="text-subtitle2">{{ job.status === 'Queued' ? $t('studio.waitingWorker') : $t('studio.rendering', { n: job.progress }) }}</div>
            <q-linear-progress :value="job.progress / 100" :indeterminate="job.status === 'Queued'" color="primary" rounded size="8px" />
            <div v-if="job.progressNote" class="text-caption text-grey-7">{{ job.progressNote }}</div>
            <div class="text-caption text-grey-6">{{ $t('studio.attempts', { n: job.attempts }) }}</div>
          </div>

          <!-- Roteiro -->
          <q-expansion-item v-model="scriptOpen" switch-toggle-side dense header-class="q-px-none text-subtitle1 text-weight-bold"
            :label="$t('studio.script')" :caption="segments.length ? $t('studio.duration', { s: seconds }) : $t('studio.noScript')">
            <div class="column no-wrap q-gutter-y-sm q-pt-sm">
              <div v-for="seg in segments" :key="seg.label" class="segment card-rounded q-pa-sm">
                <div class="text-caption text-uppercase text-grey-7">{{ segmentLabel(seg.label) }}</div>
                <div class="text-weight-bold">{{ seg.title || '·' }}</div>
                <div v-if="seg.sub" class="text-caption">{{ seg.sub }}</div>
                <div class="q-mt-xs fala" :class="{ 'text-negative': seg.fala === null }">
                  <q-icon :name="mdiMicrophoneOutline" size="16px" class="q-mr-xs" />{{ seg.fala ?? $t('studio.missingLine') }}
                </div>
              </div>

              <template v-if="canEditScript">
                <q-file :model-value="null" outlined dense multiple accept=".json" :label="$t('studio.pickJson')"
                  :hint="$t('studio.pickJsonHint')" @update:model-value="pickJson">
                  <template #prepend><q-icon :name="mdiCodeJson" /></template>
                </q-file>
                <div class="row q-col-gutter-sm">
                  <q-input v-model="roteiroText" outlined type="textarea" class="col-12 col-md-7 mono" input-style="min-height: 180px"
                    :label="$t('studio.roteiro')" />
                  <q-input v-model="falasText" outlined type="textarea" class="col-12 col-md-5 mono" input-style="min-height: 180px"
                    :label="$t('studio.falas')" />
                </div>
                <q-banner v-if="scriptErrors.length" rounded class="bg-red-1 text-negative">
                  <div class="text-weight-bold q-mb-xs">{{ $t('studio.scriptRefused') }}</div>
                  <div v-for="e in scriptErrors" :key="e">· {{ e }}</div>
                </q-banner>
                <div class="row q-gutter-sm">
                  <q-btn unelevated color="primary" no-caps :label="$t('studio.saveScript')" :loading="busy" @click="saveScript" />
                </div>
              </template>
            </div>
          </q-expansion-item>

          <template v-if="job.status === 'ScriptReady' || job.status === 'Failed'">
            <q-banner v-for="w in job.warnings" :key="w" dense rounded class="bg-amber-1 text-brown-9">{{ w }}</q-banner>
          </template>
          <div v-if="job.status === 'ScriptReady' || job.status === 'Failed'" class="row q-gutter-sm">
            <q-btn unelevated color="accent" text-color="dark" no-caps :icon="mdiPlay" :loading="busy"
              :label="job.status === 'Failed' ? $t('studio.retry') : $t('studio.render')" @click="render" />
            <div class="text-caption text-grey-7 self-center">{{ $t('studio.renderHint') }}</div>
          </div>

          <!-- Revisão -->
          <template v-if="job.status === 'Review' || job.status === 'Done'">
            <div class="text-subtitle1 text-weight-bold">{{ $t('studio.files') }}</div>
            <q-banner v-for="w in job.warnings" :key="w" dense rounded class="bg-amber-1 text-brown-9">{{ w }}</q-banner>
            <div class="row q-col-gutter-md">
              <div v-for="o in job.outputs" :key="o.role + o.url" class="col-12 col-sm-6 col-lg-4">
                <div class="output card-rounded q-pa-sm">
                  <div class="text-caption text-weight-bold q-mb-xs">{{ roleLabel(o.role) }}</div>
                  <video v-if="isVideo(o.url)" :src="o.url" controls playsinline preload="metadata" class="output-media" />
                  <img v-else :src="o.url" alt="" class="output-media" />
                  <a :href="o.url" target="_blank" rel="noopener" class="text-caption text-primary">{{ $t('studio.open') }}</a>
                </div>
              </div>
            </div>
          </template>

          <template v-if="job.status === 'Review'">
            <div class="text-subtitle1 text-weight-bold">{{ $t('studio.toPosts') }}</div>
            <q-file v-model="txt" outlined dense accept=".txt" :label="$t('studio.txt')" :hint="$t('studio.txtHint')" @update:model-value="readTxt" />
            <q-banner v-for="e in txtErrors" :key="e" dense rounded class="bg-red-1 text-negative">{{ e }}</q-banner>
            <q-input v-model="when" outlined dense type="datetime-local" stack-label :label="$t('pages.publications.when')" />
            <q-input v-model="captions.instagram" outlined type="textarea" autogrow label="Instagram" />
            <q-input v-model="captions.facebook" outlined type="textarea" autogrow label="Facebook" />
            <q-input v-model="captions.linkedin" outlined type="textarea" autogrow label="LinkedIn" />
            <div class="row items-center q-gutter-sm">
              <q-btn unelevated color="primary" no-caps :icon="mdiCalendarPlus" :label="$t('studio.createPosts')" :loading="busy"
                :disable="!when || !(captions.instagram || captions.facebook || captions.linkedin)" @click="createPosts" />
              <div class="text-caption text-grey-7">{{ $t('studio.createPostsHint') }}</div>
            </div>
          </template>

          <div v-if="job.status === 'Done'" class="row items-center q-gutter-sm">
            <q-icon :name="mdiCheckCircleOutline" color="positive" size="24px" />
            <div>{{ $t('studio.doneHint') }}</div>
            <q-btn flat no-caps color="primary" :label="$t('studio.goPublications')" :to="{ name: 'publications' }" />
          </div>
        </div>
      </div>
    </div>

    <!-- Novo job -->
    <q-dialog v-model="newOpen">
      <q-card style="width: 480px; max-width: 94vw" class="q-pa-sm">
        <q-card-section class="text-h6">{{ $t('studio.new') }}</q-card-section>
        <q-card-section class="q-gutter-md">
          <q-btn-toggle v-model="form.kind" spread no-caps unelevated rounded toggle-color="primary" :options="kindOptions" />
          <q-input v-model="form.theme" outlined :label="$t('studio.theme')" :hint="$t('studio.themeHint')" maxlength="300" />
          <q-input v-model="form.voiceKey" outlined :label="$t('studio.voice')" :hint="$t('studio.voiceHint')" maxlength="64" />
        </q-card-section>
        <q-card-actions align="right">
          <q-btn flat no-caps :label="$t('common.actions.cancel')" v-close-popup />
          <q-btn unelevated color="primary" no-caps :label="$t('studio.create')" :loading="busy" :disable="!form.theme.trim()" @click="createJob" />
        </q-card-actions>
      </q-card>
    </q-dialog>
  </q-page>
</template>

<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, reactive, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import { useQuasar } from 'quasar';
import { isAxiosError } from 'axios';
import {
  mdiPlus, mdiMovieOpenPlayOutline, mdiMicrophoneOutline, mdiCodeJson, mdiPlay, mdiCalendarPlus, mdiCheckCircleOutline,
} from '@quasar/extras/mdi-v7';
import { api } from 'src/services/api';
import type { CreativeJob, CreativeKind, Publication } from 'src/types/api';
import { useBrandStore } from 'src/stores/brandStore';
import { estimateSeconds, isActive, jobColor, jobStep, parseJson, scriptSegments } from 'src/utils/studio';
import { addDays, fromLocalInput, isVideo, localDayKey, toLocalInput } from 'src/utils/publications';
import { proposeSlots } from 'src/utils/calendar';
import { parseReelText } from 'src/utils/reelImport';

defineOptions({ name: 'StudioPage' });

const { t, locale } = useI18n();
const $q = useQuasar();
const brand = useBrandStore();

const jobs = ref<CreativeJob[]>([]);
const loading = ref(false);
const busy = ref(false);
const selectedId = ref<string | null>(null);
const job = computed(() => jobs.value.find((j) => j.id === selectedId.value) ?? null);
const base = computed(() => `/admin/brands/${brand.selected}/creative-jobs`);

const steps = ['script', 'queue', 'render', 'review', 'posts'] as const;
const step = computed(() => (job.value ? jobStep(job.value.status) : 0));
const segments = computed(() => (job.value ? scriptSegments(job.value.roteiro, job.value.falas) : []));
const seconds = computed(() => estimateSeconds(job.value?.falas));
const canEditScript = computed(() => !!job.value && ['Draft', 'ScriptReady', 'Review', 'Failed'].includes(job.value.status));
const scriptOpen = ref(true);

function fmt(iso: string) {
  return new Date(iso).toLocaleString(locale.value, { day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit' });
}

function segmentLabel(label: string) {
  if (label === 'opening') return t('studio.opening');
  if (label === 'closing') return t('studio.closing');
  return t('studio.block', { n: label.split(':')[1] });
}

function roleLabel(role: string) {
  const known: Record<string, string> = {
    'video-instagram': t('studio.roles.videoInstagram'),
    video: t('studio.roles.video'),
    'cover-instagram': t('studio.roles.cover'),
    grade: t('studio.roles.grid'),
  };
  return known[role] ?? role;
}

function apiError(e: unknown): string {
  if (isAxiosError(e) && typeof e.response?.data?.error === 'string') return e.response.data.error;
  return t('common.saveError');
}

async function load() {
  if (!brand.selected) return;
  loading.value = true;
  try {
    jobs.value = (await api.get<CreativeJob[]>(base.value)).data;
    if (!job.value && jobs.value[0]) select(jobs.value[0]);
  } catch {
    $q.notify({ type: 'negative', message: t('common.error') });
  } finally {
    loading.value = false;
  }
}

function replace(updated: CreativeJob) {
  const i = jobs.value.findIndex((j) => j.id === updated.id);
  if (i >= 0) jobs.value.splice(i, 1, updated);
  else jobs.value.unshift(updated);
}

// --- Roteiro ---
const roteiroText = ref('');
const falasText = ref('');
const scriptErrors = ref<string[]>([]);

function select(j: CreativeJob) {
  selectedId.value = j.id;
  roteiroText.value = j.roteiro ? JSON.stringify(j.roteiro, null, 2) : '';
  falasText.value = j.falas ? JSON.stringify(j.falas, null, 2) : '';
  scriptErrors.value = [];
  scriptOpen.value = j.status === 'Draft' || j.status === 'ScriptReady' || j.status === 'Failed';
  resetPosts();
}

/** Arquivos do pacote: o que tem "falas" no nome vai pras falas, o resto pro roteiro. */
async function pickJson(files: File[] | File | null) {
  const list = Array.isArray(files) ? files : files ? [files] : [];
  for (const f of list) {
    const text = await f.text();
    if (/falas/i.test(f.name)) falasText.value = text;
    else roteiroText.value = text;
  }
}

async function saveScript() {
  if (!job.value) return;
  const r = parseJson(roteiroText.value);
  const f = parseJson(falasText.value);
  scriptErrors.value = [
    ...(r.error ? [`${t('studio.roteiro')}: ${r.error}`] : []),
    ...(f.error ? [`${t('studio.falas')}: ${f.error}`] : []),
  ];
  if (scriptErrors.value.length) return;
  busy.value = true;
  try {
    const { data } = await api.put<CreativeJob>(`${base.value}/${job.value.id}/script`, { roteiro: r.value, falas: f.value });
    replace(data);
    $q.notify({ type: 'positive', message: t('studio.scriptSaved') });
  } catch (e) {
    const errors = isAxiosError(e) && e.response?.status === 422 ? (e.response.data?.errors as string[] | undefined) : undefined;
    if (errors) scriptErrors.value = errors;
    else $q.notify({ type: 'negative', message: apiError(e) });
  } finally {
    busy.value = false;
  }
}

async function render() {
  if (!job.value) return;
  busy.value = true;
  try {
    replace((await api.post<CreativeJob>(`${base.value}/${job.value.id}/render`)).data);
    $q.notify({ type: 'positive', message: t('studio.queued') });
  } catch (e) {
    $q.notify({ type: 'negative', message: apiError(e) });
  } finally {
    busy.value = false;
  }
}

// --- Revisão: vira posts ---
const txt = ref<File | null>(null);
const txtErrors = ref<string[]>([]);
const when = ref('');
const captions = reactive({ instagram: '', facebook: '', linkedin: '' });

/**
 * Próxima data livre do calendário da marca (seg, qua e sex às 12h, feriado vai pro dia seguinte, dia que já tem
 * post fica de fora). A pessoa ajusta se quiser.
 */
async function suggestWhen() {
  try {
    const now = new Date();
    const { data } = await api.get<Publication[]>(`/admin/brands/${brand.selected}/publications`, {
      params: { from: now.toISOString(), to: addDays(now, 120).toISOString() },
    });
    const slot = proposeSlots(now, 1, new Set(data.map((p) => localDayKey(new Date(p.scheduledFor)))))[0];
    if (slot) when.value = toLocalInput(slot.toISOString());
  } catch {
    // Sem a agenda, fica a sugestão sem conferir dia ocupado.
  }
}

function resetPosts() {
  txt.value = null;
  txtErrors.value = [];
  const first = proposeSlots(new Date(), 1, new Set())[0];
  when.value = first ? toLocalInput(first.toISOString()) : '';
  Object.assign(captions, { instagram: '', facebook: '', linkedin: '' });
  void suggestWhen();
}

async function readTxt(file: File | null) {
  if (!file) return;
  const blocks = parseReelText(await file.text());
  txtErrors.value = blocks.errors;
  captions.instagram = blocks.instagram ?? '';
  captions.facebook = blocks.facebook ?? '';
  captions.linkedin = blocks.linkedin ?? '';
}

async function createPosts() {
  if (!job.value) return;
  busy.value = true;
  try {
    const { data } = await api.post<{ job: CreativeJob }>(`${base.value}/${job.value.id}/publications`, {
      scheduledFor: fromLocalInput(when.value),
      instagram: captions.instagram || null,
      facebook: captions.facebook || null,
      linkedIn: captions.linkedin || null,
    });
    replace(data.job);
    $q.notify({ type: 'positive', message: t('studio.postsCreated') });
  } catch (e) {
    $q.notify({ type: 'negative', message: apiError(e) });
  } finally {
    busy.value = false;
  }
}

// --- Novo job ---
const newOpen = ref(false);
const form = reactive<{ kind: CreativeKind; theme: string; voiceKey: string }>({ kind: 'Organic', theme: '', voiceKey: '' });
const kindOptions = computed(() => (['Organic', 'Ad', 'Prospecting'] as const).map((k) => ({ value: k, label: t(`studio.kind.${k}`) })));

async function createJob() {
  busy.value = true;
  try {
    const { data } = await api.post<CreativeJob>(base.value, { kind: form.kind, theme: form.theme.trim(), voiceKey: form.voiceKey.trim() || null });
    jobs.value.unshift(data);
    select(data);
    newOpen.value = false;
    Object.assign(form, { kind: 'Organic', theme: '', voiceKey: '' });
  } catch (e) {
    $q.notify({ type: 'negative', message: apiError(e) });
  } finally {
    busy.value = false;
  }
}

// Chegou a entrega: o roteiro fecha pra dar espaço aos arquivos e às legendas.
watch(() => job.value?.status, (now, before) => {
  if (now === 'Review' && before && before !== 'Review') scriptOpen.value = false;
});

// Enquanto algum job estiver com o worker, a lista se atualiza sozinha (o progresso chega pelo servidor).
let timer: ReturnType<typeof setInterval> | undefined;
onMounted(() => {
  void load();
  timer = setInterval(() => {
    if (jobs.value.some((j) => isActive(j.status))) void load();
  }, 10_000);
});
onBeforeUnmount(() => clearInterval(timer));
watch(() => brand.selected, () => {
  jobs.value = [];
  selectedId.value = null;
  void load();
});
</script>

<style lang="scss" scoped>
.job-row, .detail, .empty, .segment, .output { border: 1px solid rgba(0, 0, 0, 0.08); background: #fff; }
.body--dark .job-row, .body--dark .detail, .body--dark .empty, .body--dark .segment, .body--dark .output {
  border-color: rgba(255, 255, 255, 0.08); background: $dark;
}
.body--dark .segment { background: rgba(255, 255, 255, 0.02); }
.empty { border-style: dashed; }
.job-row {
  cursor: pointer; outline: none; transition: transform 0.15s ease, border-color 0.15s ease, box-shadow 0.15s ease;
  &:hover { transform: translateX(2px); border-color: rgba($primary, 0.5); }
  &.selected, &:focus-visible { border-color: $primary; box-shadow: 0 0 0 3px rgba($primary, 0.18); }
}
.fala { font-style: italic; }
.mono :deep(textarea) { font-family: ui-monospace, Consolas, monospace; font-size: 12px; }
.output-media { width: 100%; max-height: 320px; object-fit: contain; border-radius: 10px; background: #05040b; display: block; }

.steps { gap: 4px; }
.step { position: relative; opacity: 0.5; }
.step-dot { width: 12px; height: 12px; border-radius: 50%; margin: 0 auto 4px; border: 2px solid currentColor; }
.step.done { opacity: 0.8; .step-dot { background: $positive; border-color: $positive; } }
.step.current { opacity: 1; color: $primary; font-weight: 600; .step-dot { background: $primary; box-shadow: 0 0 0 4px rgba($primary, 0.2); } }
</style>

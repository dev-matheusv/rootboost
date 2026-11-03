<template>
  <q-page class="q-pa-lg">
    <div class="row items-center q-mb-lg q-gutter-sm">
      <div class="col-12 col-md">
        <div class="page-eyebrow">{{ brand.selectedName }}</div>
        <div class="text-h5 sora-bold">{{ $t('voices.title') }}</div>
        <div class="text-grey-7">{{ $t('voices.subtitle') }}</div>
      </div>
      <template v-if="brand.selected">
        <q-btn outline color="primary" no-caps :icon="mdiAccountVoice" :label="$t('voices.bank')" @click="bankOpen = true" />
        <q-btn unelevated color="primary" no-caps :icon="mdiMicrophoneOutline" :label="$t('voices.record')" @click="openRecord" />
      </template>
    </div>

    <q-banner v-if="!brand.selected" rounded class="bg-blue-1 text-primary">{{ $t('voices.pickBrand') }}</q-banner>
    <div v-else-if="!loading && !voices.length" class="empty card-rounded q-pa-xl text-center text-grey-7">
      <q-icon :name="mdiMicrophoneOutline" size="44px" class="q-mb-sm" />
      <div>{{ $t('voices.empty') }}</div>
    </div>
    <div v-else class="row q-col-gutter-md">
      <div v-for="v in voices" :key="v.id" class="col-12 col-md-6">
        <div class="item card-rounded q-pa-md column q-gutter-y-sm">
          <div class="row items-center no-wrap">
            <q-icon :name="v.kind === 'Clone' ? mdiMicrophoneOutline : mdiAccountVoice" size="22px" color="primary" class="q-mr-sm" />
            <div class="col">
              <div class="text-weight-bold">{{ v.name }}</div>
              <div class="text-caption text-grey-7">
                {{ $t(`voices.kind.${v.kind}`) }}<span v-if="v.consentBy"> · {{ $t('voices.consentBy', { who: v.consentBy }) }}</span>
              </div>
            </div>
            <q-chip dense :color="statusColor(v.status)" text-color="white">{{ $t(`voices.status.${v.status}`) }}</q-chip>
            <q-btn flat round dense size="sm" :icon="mdiDeleteOutline" color="negative" :disable="v.status === 'Processing'" @click="remove(v)">
              <q-tooltip>{{ $t('voices.delete') }}</q-tooltip>
            </q-btn>
          </div>
          <div v-if="v.status === 'Queued' || v.status === 'Processing'" class="text-caption text-grey-7">
            {{ v.bankId ? $t('voices.bankWaiting') : $t('voices.waiting') }}
          </div>
          <div v-if="v.error && v.status !== 'Ready'" class="text-caption text-negative">{{ v.error }}</div>
          <template v-if="v.status === 'Ready'">
            <div v-if="v.samples.length > 1" class="text-caption text-grey-7">{{ $t('voices.listen') }}</div>
            <div v-for="seed in v.samples" :key="seed" class="row items-center no-wrap q-gutter-x-sm">
              <audio v-if="sampleUrls[`${v.id}-${seed}`]" :src="sampleUrls[`${v.id}-${seed}`]" controls preload="none" class="col" />
              <q-btn v-else flat dense no-caps color="primary" :icon="mdiPlay" :label="$t('voices.sample', { n: seed })" class="col"
                @click="loadSample(v, seed)" />
              <q-btn :outline="v.chosenSeed !== seed" unelevated dense no-caps color="primary" :icon="v.chosenSeed === seed ? mdiCheck : undefined"
                :label="v.chosenSeed === seed ? $t('voices.chosen') : $t('voices.choose')" @click="choose(v, seed)" />
            </div>
          </template>
        </div>
      </div>
    </div>

    <!-- Gravar a própria voz: autorização falada + leitura, no celular ou no computador. -->
    <q-dialog v-model="recordOpen" @hide="stopRecording(true)">
      <q-card style="width: 560px; max-width: 94vw" class="q-pa-sm">
        <q-card-section class="text-h6">{{ $t('voices.record') }}</q-card-section>
        <q-card-section class="column q-gutter-y-md">
          <div class="row q-col-gutter-sm">
            <q-input v-model="rec.person" outlined dense class="col-12 col-sm-6" :label="$t('voices.person')" maxlength="120" @blur="loadScript" />
            <q-input v-model="rec.name" outlined dense class="col-12 col-sm-6" :label="$t('voices.name')" :hint="$t('voices.nameHint')" maxlength="60" />
          </div>
          <div class="script card-rounded q-pa-md">
            <div class="text-caption text-grey-7 q-mb-xs">{{ $t('voices.readAloud') }}</div>
            <div class="text-weight-medium q-mb-sm">{{ script.consent }}</div>
            <div>{{ script.reading }}</div>
          </div>
          <div class="text-caption text-grey-7">{{ $t('voices.tips') }}</div>
          <div class="row items-center q-gutter-sm">
            <q-btn v-if="!recording" unelevated rounded color="negative" no-caps :icon="mdiMicrophoneOutline"
              :label="audio ? $t('voices.recordAgain') : $t('voices.start')" :disable="!rec.person.trim()" @click="startRecording" />
            <q-btn v-else unelevated rounded color="negative" no-caps :icon="mdiStop" :label="$t('voices.stop', { s: seconds })" @click="stopRecording()" />
            <audio v-if="audio && !recording" :src="audio.url" controls class="col" />
          </div>
          <q-checkbox v-model="rec.consent" :label="$t('voices.consentCheck')" />
        </q-card-section>
        <q-card-actions align="right">
          <q-btn flat no-caps :label="$t('common.actions.cancel')" v-close-popup />
          <q-btn unelevated color="primary" no-caps :label="$t('voices.send')" :loading="busy"
            :disable="!audio || recording || !rec.consent || !rec.name.trim() || !rec.person.trim()" @click="sendRecording" />
        </q-card-actions>
      </q-card>
    </q-dialog>

    <!-- Banco: vozes prontas com amostra (mesmo texto pra todas); ou montar outra por gênero, idade e tom. -->
    <q-dialog v-model="bankOpen" @hide="stopPreview">
      <q-card style="width: 720px; max-width: 96vw" class="q-pa-sm">
        <q-card-section class="text-h6">{{ $t('voices.bank') }}</q-card-section>
        <q-card-section class="column q-gutter-y-md">
          <div class="text-caption text-grey-7">{{ $t('voices.bankHint') }}</div>
          <div class="bank-grid">
            <div v-for="b in catalog" :key="b.id" class="bank-card card-rounded q-pa-sm column q-gutter-y-xs">
              <div class="row items-center no-wrap q-gutter-x-xs">
                <q-btn round dense flat size="sm" color="primary" :icon="previewing === b.id ? mdiStop : mdiPlay"
                  :aria-label="$t('voices.sample', { n: 1 })" @click="togglePreview(b)" />
                <div class="col ellipsis">
                  <div class="text-weight-bold">{{ b.name }}</div>
                  <div class="text-caption text-grey-7 ellipsis">
                    {{ $t(`voices.${b.gender}`) }} · {{ $t(`voices.age.${b.age}`) }} · {{ $t(`voices.pitch.${b.pitch}`) }}
                  </div>
                </div>
                <q-chip v-if="!b.approved" dense outline color="orange-8" size="sm">{{ $t('voices.pending') }}
                  <q-tooltip>{{ $t('voices.pendingHint') }}</q-tooltip>
                </q-chip>
              </div>
              <q-btn :outline="!inBrand(b)" unelevated dense no-caps color="primary" :disable="inBrand(b)" :loading="busy && picking === b.id"
                :label="inBrand(b) ? $t('voices.inBrand') : $t('voices.useVoice')" @click="useBank(b)" />
            </div>
          </div>
          <q-expansion-item dense dense-toggle switch-toggle-side :label="$t('voices.custom')" :caption="$t('voices.customHint')" header-class="q-px-none">
            <div class="column q-gutter-y-md q-pt-sm">
          <q-input v-model="bank.name" outlined dense :label="$t('voices.name')" maxlength="60" />
          <q-btn-toggle v-model="bank.gender" spread no-caps unelevated rounded toggle-color="primary"
            :options="[{ value: 'female', label: $t('voices.female') }, { value: 'male', label: $t('voices.male') }]" />
          <q-btn-toggle v-model="bank.age" spread no-caps unelevated rounded toggle-color="primary"
            :options="['young adult', 'middle-aged', 'elderly'].map((a) => ({ value: a, label: $t(`voices.age.${a}`) }))" />
          <q-btn-toggle v-model="bank.pitch" spread no-caps unelevated rounded toggle-color="primary"
            :options="['low pitch', 'moderate pitch', 'high pitch'].map((p) => ({ value: p, label: $t(`voices.pitch.${p}`) }))" />
              <div class="row justify-end">
                <q-btn unelevated color="primary" no-caps :label="$t('voices.create')" :loading="busy && !picking" :disable="!bank.name.trim()" @click="createBank" />
              </div>
            </div>
          </q-expansion-item>
        </q-card-section>
        <q-card-actions align="right">
          <q-btn flat no-caps :label="$t('common.actions.cancel')" v-close-popup />
        </q-card-actions>
      </q-card>
    </q-dialog>
  </q-page>
</template>

<script setup lang="ts">
import { onBeforeUnmount, onMounted, reactive, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import { useQuasar } from 'quasar';
import { isAxiosError } from 'axios';
import { mdiAccountVoice, mdiCheck, mdiDeleteOutline, mdiMicrophoneOutline, mdiPlay, mdiStop } from '@quasar/extras/mdi-v7';
import { api } from 'src/services/api';
import { useBrandStore } from 'src/stores/brandStore';

defineOptions({ name: 'VozesPage' });

interface Voice {
  id: string; key: string; name: string; kind: 'Clone' | 'Design'; status: 'Queued' | 'Processing' | 'Ready' | 'Failed';
  samples: number[]; chosenSeed: number | null; error: string | null; consentBy: string | null; bankId: string | null;
}

/** Voz pronta do banco; a amostra é arquivo estático do Console (voz sintética, sem dado de pessoa). */
interface BankVoice { id: string; name: string; gender: 'female' | 'male'; age: string; pitch: string; seed: number; approved: boolean }

const MAX_SECONDS = 60;
const { t } = useI18n();
const $q = useQuasar();
const brand = useBrandStore();
const voices = ref<Voice[]>([]);
const loading = ref(false);
const busy = ref(false);
const sampleUrls = reactive<Record<string, string>>({});
const base = () => `/admin/brands/${brand.selected}/voices`;
let poll: ReturnType<typeof setInterval> | undefined;

function apiError(e: unknown): string {
  if (isAxiosError(e) && typeof e.response?.data?.error === 'string') return e.response.data.error;
  return t('common.saveError');
}

function statusColor(s: Voice['status']) {
  return ({ Queued: 'grey-7', Processing: 'orange-8', Ready: 'positive', Failed: 'negative' } as const)[s];
}

async function load() {
  if (!brand.selected) return;
  loading.value = true;
  try {
    voices.value = (await api.get<Voice[]>(base())).data;
  } catch {
    $q.notify({ type: 'negative', message: t('common.error') });
  } finally {
    loading.value = false;
  }
}

/** Amostra vem com login (é a voz de uma pessoa): baixa como arquivo e toca daqui. */
async function loadSample(v: Voice, seed: number) {
  try {
    const { data } = await api.get<Blob>(`${base()}/${v.id}/samples/${seed}`, { responseType: 'blob' });
    sampleUrls[`${v.id}-${seed}`] = URL.createObjectURL(data);
  } catch (e) {
    $q.notify({ type: 'negative', message: apiError(e) });
  }
}

async function choose(v: Voice, seed: number) {
  try {
    Object.assign(v, (await api.post<Voice>(`${base()}/${v.id}/choose`, { seed })).data);
  } catch (e) {
    $q.notify({ type: 'negative', message: apiError(e) });
  }
}

function remove(v: Voice) {
  $q.dialog({ title: t('voices.delete'), message: t('voices.deleteConfirm'), cancel: true }).onOk(() => {
    void api.delete(`${base()}/${v.id}`)
      .then(() => (voices.value = voices.value.filter((x) => x.id !== v.id)))
      .catch((e: unknown) => $q.notify({ type: 'negative', message: apiError(e) }));
  });
}

// --- Gravar ---
const recordOpen = ref(false);
const rec = reactive({ person: '', name: '', consent: false });
const script = reactive({ consent: '', reading: '' });
const recording = ref(false);
const seconds = ref(0);
const audio = ref<{ blob: Blob; url: string; ext: string } | null>(null);
let recorder: MediaRecorder | null = null;
let stream: MediaStream | null = null;
let timer: ReturnType<typeof setInterval> | undefined;

async function loadScript() {
  if (!brand.selected) return;
  const { data } = await api.get<{ consent: string; reading: string }>(`${base()}/script`, { params: { person: rec.person.trim() || undefined } });
  Object.assign(script, data);
}

function openRecord() {
  Object.assign(rec, { person: '', name: '', consent: false });
  audio.value = null;
  recordOpen.value = true;
  void loadScript();
}

/** Chrome e Android gravam webm; iPhone (Safari) grava mp4. O servidor aceita os dois. */
function pickMime(): { mime: string; ext: string } {
  for (const [mime, ext] of [['audio/webm;codecs=opus', '.webm'], ['audio/webm', '.webm'], ['audio/mp4', '.m4a'], ['audio/ogg', '.ogg']] as const)
    if (typeof MediaRecorder !== 'undefined' && MediaRecorder.isTypeSupported(mime)) return { mime, ext };
  return { mime: '', ext: '.webm' };
}

async function startRecording() {
  try {
    stream = await navigator.mediaDevices.getUserMedia({ audio: { echoCancellation: true, noiseSuppression: true } });
  } catch {
    $q.notify({ type: 'negative', message: t('voices.micDenied') });
    return;
  }
  const { mime, ext } = pickMime();
  const chunks: BlobPart[] = [];
  recorder = new MediaRecorder(stream, mime ? { mimeType: mime } : undefined);
  recorder.ondataavailable = (e) => { if (e.data.size) chunks.push(e.data); };
  recorder.onstop = () => {
    const blob = new Blob(chunks, { type: recorder?.mimeType || mime || 'audio/webm' });
    if (audio.value) URL.revokeObjectURL(audio.value.url);
    audio.value = blob.size ? { blob, url: URL.createObjectURL(blob), ext } : null;
  };
  recorder.start();
  recording.value = true;
  seconds.value = 0;
  timer = setInterval(() => {
    seconds.value++;
    if (seconds.value >= MAX_SECONDS) stopRecording();
  }, 1000);
}

function stopRecording(discard = false) {
  clearInterval(timer);
  if (recorder && recorder.state !== 'inactive') {
    if (discard) recorder.onstop = null;
    recorder.stop();
  }
  stream?.getTracks().forEach((tr) => tr.stop());
  stream = null;
  recording.value = false;
}

async function sendRecording() {
  if (!audio.value) return;
  if (seconds.value < 15) {
    $q.notify({ type: 'warning', message: t('voices.tooShort') });
    return;
  }
  busy.value = true;
  try {
    const body = new FormData();
    body.append('file', audio.value.blob, `gravacao${audio.value.ext}`);
    body.append('name', rec.name.trim());
    body.append('person', rec.person.trim());
    body.append('consent', String(rec.consent));
    const { data } = await api.post<Voice>(`${base()}/clone`, body, { timeout: 5 * 60_000 });
    voices.value.unshift(data);
    recordOpen.value = false;
    $q.notify({ type: 'positive', message: t('voices.sent') });
  } catch (e) {
    $q.notify({ type: 'negative', message: apiError(e) });
  } finally {
    busy.value = false;
  }
}

// --- Banco ---
const bankOpen = ref(false);
const bank = reactive({ name: '', gender: 'female', age: 'middle-aged', pitch: 'moderate pitch' });

const catalog = ref<BankVoice[]>([]);
const picking = ref<string | null>(null);
const inBrand = (b: BankVoice) => voices.value.some((v) => v.bankId === b.id);

watch(bankOpen, (open) => {
  if (open && !catalog.value.length)
    void api.get<BankVoice[]>(`${base()}/bank`).then(({ data }) => (catalog.value = data)).catch(() => undefined);
});

async function useBank(b: BankVoice) {
  busy.value = true;
  picking.value = b.id;
  try {
    const { data } = await api.post<Voice>(`${base()}/bank`, { bankId: b.id });
    voices.value.unshift(data);
    $q.notify({ type: 'positive', message: t('voices.sent') });
  } catch (e) {
    $q.notify({ type: 'negative', message: apiError(e) });
  } finally {
    busy.value = false;
    picking.value = null;
  }
}

// Prévia do banco: arquivo do próprio Console, tocado aqui.
const previewing = ref<string | null>(null);
const preview = new Audio();
preview.onended = () => (previewing.value = null);

async function togglePreview(b: BankVoice) {
  if (previewing.value === b.id) return stopPreview();
  preview.src = `/vozes-banco/${b.id}.m4a`;
  previewing.value = b.id;
  try {
    await preview.play();
  } catch {
    previewing.value = null;
  }
}

function stopPreview() {
  preview.pause();
  previewing.value = null;
}

async function createBank() {
  busy.value = true;
  try {
    const { data } = await api.post<Voice>(`${base()}/design`, { ...bank, name: bank.name.trim() });
    voices.value.unshift(data);
    bankOpen.value = false;
    bank.name = '';
    $q.notify({ type: 'positive', message: t('voices.sent') });
  } catch (e) {
    $q.notify({ type: 'negative', message: apiError(e) });
  } finally {
    busy.value = false;
  }
}

onMounted(() => {
  void load();
  // Voz na fila: confere de tempo em tempo até o worker terminar.
  poll = setInterval(() => { if (voices.value.some((v) => v.status === 'Queued' || v.status === 'Processing')) void load(); }, 20_000);
});
onBeforeUnmount(() => {
  clearInterval(poll);
  stopRecording(true);
  stopPreview();
  Object.values(sampleUrls).forEach((u) => URL.revokeObjectURL(u));
});
watch(() => brand.selected, () => void load());
</script>

<style lang="scss" scoped>
.item, .empty { border: 1px solid rgba(0, 0, 0, 0.08); background: #fff; height: 100%; }
.body--dark .item, .body--dark .empty { border-color: rgba(255, 255, 255, 0.08); background: $dark; }
.empty { border-style: dashed; }
.script { background: rgba(122, 76, 224, 0.08); font-size: 1.05rem; line-height: 1.5; }
audio { height: 36px; min-width: 0; }
.bank-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(200px, 1fr)); gap: 8px; }
.bank-card { border: 1px solid rgba(0, 0, 0, 0.12); }
.body--dark .bank-card { border-color: rgba(255, 255, 255, 0.12); }
</style>

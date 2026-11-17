<template>
  <!-- Pauta de conteúdos (17/11): temas guardados e a fila que cria o vídeo, escreve o roteiro e manda renderizar. -->
  <q-card flat bordered class="card-rounded q-mb-lg">
    <q-expansion-item v-model="open" header-class="q-pa-md" expand-icon-class="text-primary">
      <template #header>
        <q-item-section avatar><q-icon :name="mdiFormatListChecks" color="primary" size="24px" /></q-item-section>
        <q-item-section>
          <q-item-label class="text-subtitle1 text-weight-bold">{{ $t('pauta.title') }}</q-item-label>
          <q-item-label caption>{{ $t('pauta.subtitle', { planned: counts.planned, queued: counts.queued, made: counts.made }) }}</q-item-label>
        </q-item-section>
      </template>

      <q-separator />
      <div class="q-pa-md column q-gutter-y-md">
        <div class="row items-center q-gutter-sm">
          <q-btn v-if="seed.length" outline color="primary" no-caps :icon="mdiDownload" :loading="importing" :label="$t('pauta.import')" @click="importSeed" />
          <q-btn flat color="primary" no-caps :icon="mdiPlus" :label="$t('pauta.add')" @click="openAdd" />
          <q-space />
          <q-checkbox v-model="autoVoice" :label="$t('pauta.autoVoice')" dense />
          <q-btn unelevated color="primary" no-caps :icon="mdiPlay" :disable="!selected.length" :loading="queuing"
            :label="$t('pauta.queue', { n: selected.length })" @click="queue" />
        </div>
        <div class="text-caption text-grey-7">{{ $t('pauta.hint') }}</div>

        <div v-if="!ideas.length" class="text-grey-7 text-center q-pa-md">{{ $t('pauta.empty') }}</div>
        <div v-for="g in groups" :key="g.name" class="column q-gutter-y-xs">
          <div class="text-caption text-weight-bold text-grey-8 q-mt-sm">{{ g.name }}</div>
          <div v-for="i in g.items" :key="i.id" class="row items-start no-wrap q-py-xs idea">
            <q-checkbox v-model="selected" :val="i.id" dense :disable="i.status === 'Queued' || i.status === 'InProduction'" class="q-mr-sm" />
            <div class="col">
              <div class="text-weight-medium">{{ i.title }}</div>
              <div class="text-caption text-grey-7">{{ i.request }}</div>
              <div v-if="i.note" class="text-caption text-negative">{{ i.note }}</div>
            </div>
            <q-chip dense :color="statusColor(i)" text-color="white" class="q-ml-sm">{{ statusLabel(i) }}</q-chip>
            <q-btn flat round dense size="sm" :icon="mdiDotsVertical">
              <q-menu auto-close>
                <q-list dense style="min-width: 180px">
                  <q-item clickable :disable="i.status === 'Queued'" @click="openEdit(i)"><q-item-section>{{ $t('pauta.edit') }}</q-item-section></q-item>
                  <q-item v-if="i.status === 'Failed' || i.status === 'InProduction'" clickable @click="backToPlan(i)">
                    <q-item-section>{{ $t('pauta.backToPlan') }}</q-item-section>
                  </q-item>
                  <q-item clickable :disable="i.status === 'Queued'" @click="remove(i)"><q-item-section class="text-negative">{{ $t('pauta.remove') }}</q-item-section></q-item>
                </q-list>
              </q-menu>
            </q-btn>
          </div>
        </div>
      </div>
    </q-expansion-item>

    <q-dialog v-model="editing">
      <q-card style="width: 520px; max-width: 94vw" class="q-pa-sm">
        <q-card-section class="text-h6">{{ form.id ? $t('pauta.edit') : $t('pauta.add') }}</q-card-section>
        <q-card-section class="column q-gutter-y-md">
          <q-input v-model="form.title" outlined dense maxlength="120" :label="$t('pauta.fieldTitle')" />
          <q-input v-model="form.request" outlined dense autogrow maxlength="300" counter :label="$t('pauta.fieldRequest')" :hint="$t('pauta.fieldRequestHint')" />
          <q-input v-model="form.group" outlined dense maxlength="80" :label="$t('pauta.fieldGroup')" />
        </q-card-section>
        <q-card-actions align="right">
          <q-btn flat no-caps :label="$t('pauta.cancel')" v-close-popup />
          <q-btn unelevated no-caps color="primary" :loading="saving" :label="$t('pauta.save')" @click="save" />
        </q-card-actions>
      </q-card>
    </q-dialog>
  </q-card>
</template>

<script setup lang="ts">
import { computed, onMounted, onUnmounted, reactive, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import { useQuasar } from 'quasar';
import { isAxiosError } from 'axios';
import { mdiDotsVertical, mdiDownload, mdiFormatListChecks, mdiPlay, mdiPlus } from '@quasar/extras/mdi-v7';
import { api } from 'src/services/api';
import seedMedlycare from 'src/data/pauta-medlycare.json';

defineOptions({ name: 'PautaCard' });

interface Idea {
  id: string; title: string; request: string; group: string | null; order: number; status: 'Planned' | 'Queued' | 'InProduction' | 'Failed';
  autoVoice: boolean; jobId: string | null; note: string | null; jobStatus: string | null; jobProgress: number | null;
}

const props = defineProps<{ brandKey: string }>();
const emit = defineEmits<{ queued: [] }>();
const { t } = useI18n();
const $q = useQuasar();
const ideas = ref<Idea[]>([]);
const selected = ref<string[]>([]);
const open = ref(false);
const autoVoice = ref(true);
const importing = ref(false);
const queuing = ref(false);
const editing = ref(false);
const saving = ref(false);
const form = reactive<{ id: string | null; title: string; request: string; group: string }>({ id: null, title: '', request: '', group: '' });

// Roadmap pronto por marca (docs/CONTEUDOS-MEDLYCARE.md).
const seed = computed(() => (props.brandKey === 'medlycare' ? seedMedlycare : []));
const counts = computed(() => ({
  planned: ideas.value.filter((i) => i.status === 'Planned' || i.status === 'Failed').length,
  queued: ideas.value.filter((i) => i.status === 'Queued').length,
  made: ideas.value.filter((i) => i.status === 'InProduction').length,
}));
const groups = computed(() => {
  const map = new Map<string, Idea[]>();
  for (const i of ideas.value) {
    const g = i.group || t('pauta.noGroup');
    map.set(g, [...(map.get(g) ?? []), i]);
  }
  return [...map.entries()].map(([name, items]) => ({ name, items }));
});

function statusLabel(i: Idea) {
  if (i.status === 'InProduction' && i.jobStatus) return t(`pauta.job.${i.jobStatus}`, i.jobStatus);
  return t(`pauta.status.${i.status}`);
}
function statusColor(i: Idea) {
  if (i.status === 'Failed' || i.jobStatus === 'Failed') return 'negative';
  if (i.jobStatus === 'Review' || i.jobStatus === 'Done') return 'positive';
  if (i.status === 'Queued' || i.status === 'InProduction') return 'primary';
  return 'grey-6';
}
function fail(e: unknown) {
  const msg = isAxiosError(e) && typeof e.response?.data?.error === 'string' ? e.response.data.error : t('pauta.error');
  $q.notify({ type: 'negative', message: msg });
}

async function load() {
  try { ideas.value = (await api.get<Idea[]>(`/admin/brands/${props.brandKey}/pauta`)).data; } catch (e) { fail(e); }
}

async function importSeed() {
  importing.value = true;
  try {
    const { data } = await api.post<{ added: number }>(`/admin/brands/${props.brandKey}/pauta/import`,
      { items: seed.value.map((s) => ({ title: s.title, request: s.request, group: s.group })) });
    $q.notify({ type: 'positive', message: t('pauta.imported', { n: data.added }) });
    await load();
  } catch (e) { fail(e); } finally { importing.value = false; }
}

async function queue() {
  queuing.value = true;
  try {
    const { data } = await api.post<{ queued: number; skipped: string[] }>(`/admin/brands/${props.brandKey}/pauta/queue`,
      { ids: selected.value, autoVoice: autoVoice.value });
    $q.notify({ type: 'positive', message: t('pauta.queued', { n: data.queued }) });
    selected.value = [];
    await load();
    emit('queued');
  } catch (e) { fail(e); } finally { queuing.value = false; }
}

function openAdd() { Object.assign(form, { id: null, title: '', request: '', group: '' }); editing.value = true; }
function openEdit(i: Idea) { Object.assign(form, { id: i.id, title: i.title, request: i.request, group: i.group ?? '' }); editing.value = true; }

async function save() {
  saving.value = true;
  try {
    const body = { title: form.title, request: form.request, group: form.group || null };
    if (form.id) await api.put(`/admin/brands/${props.brandKey}/pauta/${form.id}`, body);
    else await api.post(`/admin/brands/${props.brandKey}/pauta`, body);
    editing.value = false;
    await load();
  } catch (e) { fail(e); } finally { saving.value = false; }
}

async function backToPlan(i: Idea) {
  try { await api.post(`/admin/brands/${props.brandKey}/pauta/${i.id}/plan`); await load(); } catch (e) { fail(e); }
}

function remove(i: Idea) {
  $q.dialog({ title: t('pauta.remove'), message: t('pauta.removeConfirm', { title: i.title }), cancel: true }).onOk(() => {
    void api.delete(`/admin/brands/${props.brandKey}/pauta/${i.id}`).then(load).catch(fail);
  });
}

// Enquanto houver tema na fila, atualiza a cada 20 s pra mostrar quando vira vídeo.
let timer: ReturnType<typeof setInterval> | undefined;
onMounted(() => {
  void load();
  timer = setInterval(() => { if (ideas.value.some((i) => i.status === 'Queued' || i.status === 'InProduction')) void load(); }, 20_000);
});
onUnmounted(() => clearInterval(timer));
watch(() => props.brandKey, () => { selected.value = []; void load(); });
</script>

<style scoped lang="scss">
.idea { border-bottom: 1px solid rgba(0, 0, 0, 0.06); }
.body--dark .idea { border-color: rgba(255, 255, 255, 0.06); }
</style>

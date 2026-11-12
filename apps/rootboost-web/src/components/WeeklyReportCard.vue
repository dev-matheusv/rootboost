<template>
  <!-- Resumo da semana (RB-34): geral, orgânico e pago separados (12/11), o que fazer agora e, quando a pessoa pede,
       sugestões da IA com base nos números. -->
  <q-card v-if="report" flat bordered class="card-rounded q-pa-md q-mb-xl">
    <div class="row items-center q-col-gutter-sm q-mb-sm">
      <div class="col-12 col-sm text-subtitle1 text-weight-bold">{{ $t('weeklyReport.title') }}</div>
      <div class="col-12 col-sm-auto text-caption text-grey-7">{{ formatDate(report.since) }} a {{ formatDate(report.until) }}</div>
    </div>
    <div class="row q-col-gutter-lg">
      <div v-for="s in sections" :key="s.key" class="col-12 col-md-6">
        <div class="text-caption text-grey-7 q-mb-xs">{{ $t(`weeklyReport.${s.key}`) }}</div>
        <div v-for="(l, i) in s.lines" :key="s.key + i" class="row no-wrap items-start q-mb-xs">
          <q-icon :name="icon(l.kind)" :color="color(l.kind)" size="18px" class="q-mr-sm q-mt-xs" />
          <div>{{ l.text }}</div>
        </div>
      </div>
    </div>

    <q-separator class="q-my-md" />
    <div class="row items-center q-col-gutter-sm">
      <div class="col-12 col-sm">
        <div class="text-subtitle2 text-weight-bold">{{ $t('weeklyReport.advice.title') }}</div>
        <div class="text-caption text-grey-7">{{ $t('weeklyReport.advice.subtitle') }}</div>
      </div>
      <div class="col-12 col-sm-auto">
        <q-btn unelevated color="primary" no-caps class="full-width" :icon="mdiCreationOutline" :loading="advising"
          :label="advice ? $t('weeklyReport.advice.again') : $t('weeklyReport.advice.ask')" @click="askAdvice" />
      </div>
    </div>
    <div v-if="advice" class="q-mt-md">
      <div v-if="!advice.suggestions.length" class="text-caption text-grey-7">{{ $t('weeklyReport.advice.none') }}</div>
      <div v-for="(s, i) in advice.suggestions" :key="'s' + i" class="q-mb-md">
        <div class="row items-center q-gutter-xs">
          <q-chip dense outline color="primary">{{ $t(`weeklyReport.advice.area.${s.area}`) }}</q-chip>
          <span class="text-weight-bold">{{ s.title }}</span>
        </div>
        <div class="text-body2">{{ s.why }}</div>
        <div class="text-body2 text-primary">{{ s.action }}</div>
      </div>
    </div>
  </q-card>
</template>

<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue';
import { useQuasar } from 'quasar';
import { isAxiosError } from 'axios';
import { useI18n } from 'vue-i18n';
import { mdiAlertCircleOutline, mdiCheckCircleOutline, mdiInformationOutline, mdiCloseCircleOutline, mdiCreationOutline } from '@quasar/extras/mdi-v7';
import { api } from 'src/services/api';
import { formatDate } from 'src/utils/format';

defineOptions({ name: 'WeeklyReportCard' });

interface ReportLine { text: string; kind: 'info' | 'good' | 'warning' | 'bad' }
interface WeeklyReport { since: string; until: string; highlights: ReportLine[]; nextSteps: ReportLine[]; organic: ReportLine[]; paid: ReportLine[] }
interface Suggestion { title: string; why: string; action: string; area: 'organico' | 'pago' | 'conteudo' | 'geral' }

const props = defineProps<{ brandKey: string }>();
const { t } = useI18n();
const $q = useQuasar();
const report = ref<WeeklyReport | null>(null);
const advice = ref<{ suggestions: Suggestion[] } | null>(null);
const advising = ref(false);

const sections = computed(() => report.value ? [
  { key: 'highlights', lines: report.value.highlights },
  { key: 'nextSteps', lines: report.value.nextSteps },
  { key: 'organic', lines: report.value.organic ?? [] },
  { key: 'paid', lines: report.value.paid ?? [] },
] : []);

const icon = (k: ReportLine['kind']) => ({ info: mdiInformationOutline, good: mdiCheckCircleOutline, warning: mdiAlertCircleOutline, bad: mdiCloseCircleOutline })[k];
const color = (k: ReportLine['kind']) => ({ info: 'primary', good: 'positive', warning: 'warning', bad: 'negative' })[k];

async function load() {
  advice.value = null;
  try {
    report.value = (await api.get<WeeklyReport>(`/admin/brands/${props.brandKey}/report`)).data;
  } catch {
    report.value = null; // resumo é extra: falha aqui não derruba a Visão geral
  }
}

async function askAdvice() {
  advising.value = true;
  try {
    advice.value = (await api.post<{ suggestions: Suggestion[] }>(`/admin/brands/${props.brandKey}/report/advice`, null, { timeout: 120_000 })).data;
  } catch (e) {
    const msg = isAxiosError(e) && typeof e.response?.data?.error === 'string' ? e.response.data.error : t('weeklyReport.advice.error');
    $q.notify({ type: 'negative', message: msg });
  } finally { advising.value = false; }
}

watch(() => props.brandKey, () => void load());
onMounted(() => void load());
</script>

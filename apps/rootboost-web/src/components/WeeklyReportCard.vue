<template>
  <!-- Resumo da semana (RB-34): o que saiu, o que vem e o que fazer agora. Só dado do banco, sem IA. -->
  <q-card v-if="report" flat bordered class="card-rounded q-pa-md q-mb-xl">
    <div class="row items-center q-mb-sm">
      <div class="col text-subtitle1 text-weight-bold">{{ $t('weeklyReport.title') }}</div>
      <div class="text-caption text-grey-7">{{ formatDate(report.since) }} a {{ formatDate(report.until) }}</div>
    </div>
    <div class="row q-col-gutter-lg">
      <div class="col-12 col-md-6">
        <div class="text-caption text-grey-7 q-mb-xs">{{ $t('weeklyReport.highlights') }}</div>
        <div v-for="(l, i) in report.highlights" :key="'h' + i" class="row no-wrap items-start q-mb-xs">
          <q-icon :name="icon(l.kind)" :color="color(l.kind)" size="18px" class="q-mr-sm q-mt-xs" />
          <div>{{ l.text }}</div>
        </div>
      </div>
      <div class="col-12 col-md-6">
        <div class="text-caption text-grey-7 q-mb-xs">{{ $t('weeklyReport.nextSteps') }}</div>
        <div v-for="(l, i) in report.nextSteps" :key="'n' + i" class="row no-wrap items-start q-mb-xs">
          <q-icon :name="icon(l.kind)" :color="color(l.kind)" size="18px" class="q-mr-sm q-mt-xs" />
          <div>{{ l.text }}</div>
        </div>
      </div>
    </div>
  </q-card>
</template>

<script setup lang="ts">
import { onMounted, ref, watch } from 'vue';
import { mdiAlertCircleOutline, mdiCheckCircleOutline, mdiInformationOutline, mdiCloseCircleOutline } from '@quasar/extras/mdi-v7';
import { api } from 'src/services/api';
import { formatDate } from 'src/utils/format';

defineOptions({ name: 'WeeklyReportCard' });

interface ReportLine { text: string; kind: 'info' | 'good' | 'warning' | 'bad' }
interface WeeklyReport { since: string; until: string; highlights: ReportLine[]; nextSteps: ReportLine[] }

const props = defineProps<{ brandKey: string }>();
const report = ref<WeeklyReport | null>(null);

const icon = (k: ReportLine['kind']) => ({ info: mdiInformationOutline, good: mdiCheckCircleOutline, warning: mdiAlertCircleOutline, bad: mdiCloseCircleOutline })[k];
const color = (k: ReportLine['kind']) => ({ info: 'primary', good: 'positive', warning: 'warning', bad: 'negative' })[k];

async function load() {
  try {
    report.value = (await api.get<WeeklyReport>(`/admin/brands/${props.brandKey}/report`)).data;
  } catch {
    report.value = null; // resumo é extra: falha aqui não derruba a Visão geral
  }
}

watch(() => props.brandKey, () => void load());
onMounted(() => void load());
</script>

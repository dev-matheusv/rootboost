<template>
  <q-page class="q-pa-lg">
    <div class="row items-end q-mb-lg q-gutter-sm">
      <div class="col">
        <div class="text-h5 sora-bold">{{ $t('pages.campaigns.title') }}</div>
        <div class="text-grey-7">{{ $t('pages.campaigns.subtitle') }}</div>
      </div>
      <q-btn-toggle v-if="brand.selected" v-model="days" no-caps rounded unelevated toggle-color="primary" :options="windows" @update:model-value="load" />
      <q-btn v-if="brand.selected" flat round :icon="mdiRefresh" :loading="loading" @click="load">
        <q-tooltip>{{ $t('common.actions.refresh') }}</q-tooltip>
      </q-btn>
    </div>

    <q-banner v-if="!brand.selected" rounded class="bg-blue-1 text-primary">{{ $t('pages.campaigns.pickBrand') }}</q-banner>

    <template v-else>
      <q-banner v-if="error" rounded class="bg-red-1 text-negative q-mb-md">
        {{ $t('common.error') }}
        <template #action><q-btn flat :label="$t('common.actions.retry')" @click="load" /></template>
      </q-banner>

      <q-banner v-if="state !== 'live'" rounded class="q-mb-md" :class="state === 'fake' ? 'bg-amber-1 text-brown-9' : 'bg-blue-1 text-primary'">
        <template #avatar><q-icon :name="mdiInformationOutline" /></template>
        {{ $t(`pages.campaigns.state.${state}`) }}
      </q-banner>

      <q-banner v-for="a in summary?.alerts ?? []" :key="a.kind + a.objectId" rounded class="bg-red-1 text-negative q-mb-sm">
        <template #avatar><q-icon :name="mdiAlertOctagonOutline" /></template>
        <span v-if="a.name" class="text-weight-bold">{{ a.name }}: </span>{{ alertText(a) }}
      </q-banner>

      <div v-if="currency" class="row q-col-gutter-md q-mb-lg q-mt-xs">
        <div v-for="kpi in kpis" :key="kpi.label" class="col-6 col-md-4 col-lg-2">
          <q-card flat class="card-rounded q-pa-md" :class="$q.dark.isActive ? 'card-bordered-dark' : 'card-bordered-light'">
            <div class="text-caption text-grey-7">{{ $t(kpi.label) }}</div>
            <q-skeleton v-if="loading && !summary" type="text" width="70px" />
            <div v-else class="kpi-value">{{ kpi.value }}</div>
          </q-card>
        </div>
      </div>

      <!-- Gasto por dia: barra simples, sem biblioteca de gráfico. -->
      <q-card v-if="bars.length" flat bordered class="card-rounded q-pa-md q-mb-lg">
        <div class="text-subtitle2 q-mb-sm">{{ $t('pages.campaigns.spendPerDay') }}</div>
        <div class="row no-wrap bars">
          <div v-for="b in bars" :key="b.date" class="col row items-end justify-center">
            <div class="bar bg-primary" :style="{ height: `${Math.max(b.height * 100, 2)}%` }">
              <q-tooltip>
                {{ formatDate(b.date) }}: {{ money(b.spend) }} · {{ b.conversions }} {{ $t('pages.campaigns.conversions').toLowerCase() }}
              </q-tooltip>
            </div>
          </div>
        </div>
        <div class="row no-wrap q-mt-xs">
          <div v-for="b in bars" :key="b.date" class="col text-center text-caption text-grey-7">{{ formatDate(b.date).slice(0, 5) }}</div>
        </div>
      </q-card>

      <q-table flat bordered class="card-rounded" :rows="summary?.campaigns ?? []" :columns="columns" row-key="campaignId"
        :loading="loading" :no-data-label="$t('pages.campaigns.empty')" :pagination="{ rowsPerPage: 0 }" hide-pagination />
    </template>
  </q-page>
</template>

<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import type { QTableColumn } from 'quasar';
import { mdiRefresh, mdiInformationOutline, mdiAlertOctagonOutline } from '@quasar/extras/mdi-v7';
import { api } from 'src/services/api';
import type { AdMetrics, AdsAccounts, AdsAlert, AdsSummary } from 'src/types/api';
import { useBrandStore } from 'src/stores/brandStore';
import { connectionState, singleCurrency, spendBars } from 'src/utils/ads';
import { formatDate, formatInt, formatMoney, formatPercent } from 'src/utils/format';

defineOptions({ name: 'CampaignsPage' });

type Row = AdsSummary['campaigns'][number];

const { t } = useI18n();
const brand = useBrandStore();
const days = ref(7);
const summary = ref<AdsSummary | null>(null);
const accounts = ref<AdsAccounts | null>(null);
const loading = ref(false);
const error = ref(false);

const windows = computed(() => [7, 14, 30].map((d) => ({ value: d, label: t('pages.campaigns.days', { n: d }) })));
const currency = computed(() => singleCurrency(summary.value));
const bars = computed(() => spendBars(summary.value));
const state = computed(() => connectionState(
  accounts.value?.reader ?? 'None',
  accounts.value?.accounts.filter((a) => a.brandKey === brand.selected).length ?? 0,
));

const dash = '·';
const money = (v: number | null, cur = currency.value ?? 'BRL') => (v === null ? dash : formatMoney(v, cur));
const pct = (v: number | null) => (v === null ? dash : formatPercent(v));

/** A regra de parada vem com o valor na moeda da campanha, formatado aqui no padrão da tela. */
function alertText(a: AdsAlert): string {
  if (a.kind !== 'stopRule') return a.message;
  const c = summary.value?.campaigns.find((x) => x.campaignId === a.objectId);
  return c ? t('pages.campaigns.stopRule', { spend: money(c.metrics.spend, c.currency) }) : a.message;
}

const kpis = computed(() => {
  const m: AdMetrics | undefined = summary.value?.totals;
  return [
    { label: 'pages.campaigns.spend', value: m ? money(m.spend) : dash },
    { label: 'pages.campaigns.conversions', value: m ? formatInt(m.conversions) : dash },
    { label: 'pages.campaigns.cpa', value: m ? money(m.cpa) : dash },
    { label: 'pages.campaigns.ctr', value: m ? pct(m.ctr) : dash },
    { label: 'pages.campaigns.cpc', value: m ? money(m.cpc) : dash },
    { label: 'pages.campaigns.impressions', value: m ? formatInt(m.impressions) : dash },
  ];
});

const columns = computed<QTableColumn<Row>[]>(() => [
  { name: 'name', label: t('pages.campaigns.campaign'), field: 'name', align: 'left' },
  { name: 'spend', label: t('pages.campaigns.spend'), field: (r) => r.metrics.spend, format: (_v, r) => money(r.metrics.spend, r.currency), align: 'right', sortable: true },
  { name: 'conversions', label: t('pages.campaigns.conversions'), field: (r) => r.metrics.conversions, align: 'right', sortable: true },
  { name: 'cpa', label: t('pages.campaigns.cpa'), field: (r) => r.metrics.cpa ?? Infinity, format: (_v, r) => money(r.metrics.cpa, r.currency), align: 'right', sortable: true },
  { name: 'ctr', label: t('pages.campaigns.ctr'), field: (r) => r.metrics.ctr ?? 0, format: (_v, r) => pct(r.metrics.ctr), align: 'right', sortable: true },
  { name: 'cpc', label: t('pages.campaigns.cpc'), field: (r) => r.metrics.cpc ?? Infinity, format: (_v, r) => money(r.metrics.cpc, r.currency), align: 'right' },
  { name: 'impressions', label: t('pages.campaigns.impressions'), field: (r) => r.metrics.impressions, format: (v: number) => formatInt(v), align: 'right' },
]);

async function load() {
  if (!brand.selected) return;
  loading.value = true;
  error.value = false;
  try {
    const [s, a] = await Promise.all([
      api.get<AdsSummary>(`/admin/brands/${brand.selected}/ads/summary`, { params: { days: days.value } }),
      api.get<AdsAccounts>('/admin/ads/accounts'),
    ]);
    summary.value = s.data;
    accounts.value = a.data;
  } catch {
    error.value = true;
  } finally {
    loading.value = false;
  }
}

onMounted(load);
watch(() => brand.selected, () => {
  summary.value = null;
  void load();
});
</script>

<style lang="scss" scoped>
.bars { height: 140px; gap: 6px; }
.bar { width: 100%; max-width: 36px; border-radius: 6px 6px 2px 2px; min-height: 2px; }
</style>

<template>
  <q-page class="q-pa-lg">
    <div class="row items-end q-mb-lg q-gutter-sm">
      <div class="col">
        <div class="text-h5 sora-bold">{{ $t('pages.overview.title') }}</div>
        <div class="text-grey-7">{{ $t('pages.overview.subtitle') }}</div>
      </div>
      <q-btn-toggle v-model="days" no-caps rounded unelevated toggle-color="primary" :options="windows" @update:model-value="load" />
      <q-btn flat round :icon="mdiRefresh" :loading="loading" @click="load">
        <q-tooltip>{{ $t('common.actions.refresh') }}</q-tooltip>
      </q-btn>
    </div>

    <q-banner v-if="error" rounded class="bg-red-1 text-negative q-mb-md">
      {{ $t('common.error') }}
      <template #action><q-btn flat :label="$t('common.actions.retry')" @click="load" /></template>
    </q-banner>

    <div class="row q-col-gutter-md q-mb-lg">
      <div v-for="kpi in kpis" :key="kpi.label" class="col-12 col-sm-6 col-lg-3">
        <q-card flat class="card-rounded q-pa-md" :class="$q.dark.isActive ? 'card-bordered-dark' : 'card-bordered-light'">
          <div class="row items-center no-wrap">
            <q-avatar rounded size="44px" :class="$q.dark.isActive ? 'dark-focus' : 'light-focus'">
              <q-icon :name="kpi.icon" color="primary" />
            </q-avatar>
            <div class="q-ml-md">
              <div class="text-caption text-grey-7">{{ $t(kpi.label) }}</div>
              <q-skeleton v-if="loading && !report" type="text" width="90px" />
              <div v-else class="kpi-value">{{ kpi.value }}</div>
            </div>
          </div>
        </q-card>
      </div>
    </div>

    <q-table flat bordered class="card-rounded" :rows="report?.products ?? []" :columns="columns" row-key="productKey"
      :loading="loading" :no-data-label="$t('common.empty')" :pagination="{ rowsPerPage: 0 }" hide-pagination>
      <template #body-cell-productKey="props">
        <q-td :props="props">
          <div class="montserrat-bold">{{ props.row.displayName ?? props.row.productKey }}</div>
          <div class="text-caption text-grey-7">{{ props.row.productKey }}</div>
        </q-td>
      </template>
      <template #body-cell-conversionRate="props">
        <q-td :props="props">
          {{ props.row.conversionRate === null ? $t('pages.overview.noData') : formatPercent(props.row.conversionRate) }}
          <q-icon v-if="!props.row.enoughData" :name="mdiInformationOutline" color="warning" class="q-ml-xs">
            <q-tooltip>{{ $t('pages.overview.notEnoughData') }}</q-tooltip>
          </q-icon>
        </q-td>
      </template>
      <template #body-cell-fulfillable="props">
        <q-td :props="props">
          <q-chip dense square :color="props.row.fulfillable ? 'positive' : 'grey-5'" text-color="white">
            {{ props.row.fulfillable ? $t('yes') : $t('no') }}
          </q-chip>
        </q-td>
      </template>
    </q-table>
  </q-page>
</template>

<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import type { QTableColumn } from 'quasar';
import {
  mdiRefresh, mdiEyeOutline, mdiCartOutline, mdiCashMultiple, mdiTrendingUp, mdiInformationOutline,
} from '@quasar/extras/mdi-v7';
import { api } from 'src/services/api';
import type { PerformanceReport, ProductPerformance } from 'src/types/api';
import { formatInt, formatMoney, formatPercent, sumByCurrency } from 'src/utils/format';
import { brandParams, useBrandStore } from 'src/stores/brandStore';

defineOptions({ name: 'OverviewPage' });

const { t } = useI18n();
const report = ref<PerformanceReport | null>(null);
const loading = ref(false);
const error = ref(false);
const days = ref<number>(0);
const brand = useBrandStore();

const windows = computed(() => [
  { label: t('pages.overview.window.all'), value: 0 },
  { label: t('pages.overview.window.d30'), value: 30 },
  { label: t('pages.overview.window.d7'), value: 7 },
]);

async function load() {
  loading.value = true;
  error.value = false;
  try {
    const params = { ...brandParams(brand.selected), ...(days.value > 0 ? { days: days.value } : {}) };
    const { data } = await api.get<PerformanceReport>('/admin/products/performance', { params });
    report.value = data;
  } catch {
    error.value = true;
  } finally {
    loading.value = false;
  }
}

const kpis = computed(() => {
  const products = report.value?.products ?? [];
  const views = products.reduce((sum, p) => sum + p.views, 0);
  const orders = products.reduce((sum, p) => sum + p.orders, 0);
  // Pedidos podem vir em moedas diferentes; somar USD com EUR mentiria.
  const revenue = Object.entries(sumByCurrency(products, (p) => p.revenue, (p) => p.currency))
    .map(([currency, value]) => formatMoney(value, currency))
    .join(' + ') || formatMoney(0, 'USD');
  return [
    { label: 'pages.overview.views', icon: mdiEyeOutline, value: formatInt(views) },
    { label: 'pages.overview.orders', icon: mdiCartOutline, value: formatInt(orders) },
    { label: 'pages.overview.revenue', icon: mdiCashMultiple, value: revenue },
    { label: 'pages.overview.conversion', icon: mdiTrendingUp, value: formatPercent(views > 0 ? orders / views : 0) },
  ];
});

const columns = computed<QTableColumn<ProductPerformance>[]>(() => [
  { name: 'productKey', label: t('pages.overview.product'), field: 'productKey', align: 'left' },
  { name: 'views', label: t('pages.overview.views'), field: 'views', format: (v: number) => formatInt(v), align: 'right', sortable: true },
  { name: 'orders', label: t('pages.overview.orders'), field: 'orders', align: 'right', sortable: true },
  { name: 'conversionRate', label: t('pages.overview.conversion'), field: 'conversionRate', align: 'right', sortable: true },
  { name: 'revenue', label: t('pages.overview.revenue'), field: (r) => formatMoney(r.revenue, r.currency), align: 'right' },
  { name: 'revenuePerVisitor', label: t('pages.overview.perVisitor'), field: (r) => (r.revenuePerVisitor === null ? t('pages.overview.noData') : formatMoney(r.revenuePerVisitor, r.currency)), align: 'right' },
  { name: 'fulfillable', label: t('pages.overview.fulfillable'), field: 'fulfillable', align: 'center' },
]);

onMounted(load);
watch(() => brand.selected, load);
</script>

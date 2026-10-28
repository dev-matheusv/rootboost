<template>
  <q-page class="q-pa-lg">
    <!-- Hoje: o que precisa de você agora, antes dos números da loja. -->
    <div class="q-mb-lg">
      <div class="page-eyebrow">{{ todayLabel }}</div>
      <div class="text-h5 sora-bold">{{ $t(`pages.overview.greeting.${greeting}`, { name: firstName(auth.userName) }) }}</div>
    </div>

    <div class="row q-col-gutter-md q-mb-xl">
      <div class="col-12 col-sm-6 col-lg-3">
        <router-link :to="{ name: 'publications' }" class="today-card card-rounded column q-pa-md" :class="{ attention: needs > 0 }">
          <q-icon :name="mdiCheckDecagramOutline" size="26px" :color="needs > 0 ? 'warning' : 'positive'" />
          <div class="kpi-value q-mt-sm">{{ brand.selected ? needs : '·' }}</div>
          <div class="text-weight-medium">{{ $t('pages.overview.today.needs') }}</div>
          <div class="text-caption text-grey-7">{{ needsHint }}</div>
        </router-link>
      </div>
      <div class="col-12 col-sm-6 col-lg-3">
        <router-link :to="{ name: 'publications' }" class="today-card card-rounded column q-pa-md">
          <q-icon :name="mdiRocketLaunchOutline" size="26px" color="primary" />
          <div class="text-subtitle1 text-weight-bold q-mt-sm ellipsis">{{ next ? next.title : $t('pages.overview.today.nothingNext') }}</div>
          <div class="text-weight-medium">{{ $t('pages.overview.today.next') }}</div>
          <div class="text-caption text-grey-7">{{ nextHint }}</div>
        </router-link>
      </div>
      <div class="col-12 col-sm-6 col-lg-3">
        <router-link :to="{ name: 'campaigns' }" class="today-card card-rounded column q-pa-md">
          <q-icon :name="mdiBullhornVariantOutline" size="26px" color="accent" />
          <div class="kpi-value q-mt-sm">{{ adsSpend ?? '·' }}</div>
          <div class="text-weight-medium">{{ $t('pages.overview.today.ads') }}</div>
          <div class="text-caption text-grey-7">{{ adsHint }}</div>
        </router-link>
      </div>
      <div class="col-12 col-sm-6 col-lg-3">
        <button type="button" class="today-card card-rounded column q-pa-md text-left" :class="{ danger: alerts > 0 }" @click="openAlerts">
          <q-icon :name="alerts > 0 ? mdiBellAlertOutline : mdiShieldCheckOutline" size="26px" :color="alerts > 0 ? 'negative' : 'positive'" />
          <div class="kpi-value q-mt-sm">{{ alerts }}</div>
          <div class="text-weight-medium">{{ $t('alerts.title') }}</div>
          <div class="text-caption text-grey-7">{{ alerts ? $t('pages.overview.today.alertsHint') : $t('pages.overview.today.noAlerts') }}</div>
        </button>
      </div>
    </div>

    <div class="row items-end q-mb-md q-gutter-sm">
      <div class="col">
        <div class="text-h6 sora-bold">{{ $t('pages.overview.store') }}</div>
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
  mdiCheckDecagramOutline, mdiRocketLaunchOutline, mdiBullhornVariantOutline, mdiBellAlertOutline, mdiShieldCheckOutline,
} from '@quasar/extras/mdi-v7';
import { api } from 'src/services/api';
import type { AdsSummary, OpsAlert, PerformanceReport, ProductPerformance, Publication } from 'src/types/api';
import { useAuthStore } from 'src/stores/authStore';
import { addDays, bundlePosts, queueSections } from 'src/utils/publications';
import { singleCurrency } from 'src/utils/ads';
import { OPEN_ALERTS_EVENT, firstName, greetingKey } from 'src/utils/overview';
import { formatInt, formatMoney, formatPercent, sumByCurrency } from 'src/utils/format';
import { brandParams, useBrandStore } from 'src/stores/brandStore';

defineOptions({ name: 'OverviewPage' });

const { t, locale } = useI18n();
const auth = useAuthStore();
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

const greeting = greetingKey(new Date().getHours());
const todayLabel = computed(() => new Date().toLocaleDateString(locale.value, { weekday: 'long', day: 'numeric', month: 'long' }));

const publications = ref<Publication[]>([]);
const ads = ref<AdsSummary | null>(null);
const alerts = ref(0);

const sections = computed(() => queueSections(bundlePosts(publications.value)));
const needs = computed(() => sections.value.needs.length);
const next = computed(() => sections.value.scheduled[0] ?? null);

const needsHint = computed(() => {
  if (!brand.selected) return t('pages.overview.today.pickBrand');
  return needs.value ? t('pages.overview.today.needsHint') : t('pages.overview.today.allClear');
});
const nextHint = computed(() => {
  const n = next.value;
  if (!n) return t('pages.overview.today.nothingNextHint');
  return `${fmtWhen(n.scheduledFor)} · ${n.items.map((p) => p.channel).join(', ')}`;
});
const adsSpend = computed(() => {
  const currency = singleCurrency(ads.value);
  return ads.value && currency ? formatMoney(ads.value.totals.spend, currency) : null;
});
const adsHint = computed(() => {
  if (!brand.selected) return t('pages.overview.today.pickBrand');
  if (!ads.value || !ads.value.daily.length) return t('pages.overview.today.adsNone');
  return t('pages.overview.today.adsHint', { n: formatInt(ads.value.totals.conversions) });
});

function fmtWhen(iso: string): string {
  return new Date(iso).toLocaleString(locale.value, { weekday: 'short', day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit' });
}

function openAlerts() {
  window.dispatchEvent(new Event(OPEN_ALERTS_EVENT));
}

/** Cartões de hoje. Cada um falha sozinho: sem anúncio ou sem post não derruba a tela. */
async function loadToday() {
  const now = new Date();
  const key = brand.selected;
  const [pubs, adsRes, alertsRes] = await Promise.allSettled([
    key
      ? api.get<Publication[]>(`/admin/brands/${key}/publications`, { params: { from: addDays(now, -1).toISOString(), to: addDays(now, 120).toISOString() } })
      : Promise.resolve({ data: [] as Publication[] }),
    key ? api.get<AdsSummary>(`/admin/brands/${key}/ads/summary`, { params: { days: 7 } }) : Promise.resolve({ data: null }),
    api.get<OpsAlert[]>('/admin/alerts'),
  ]);
  publications.value = pubs.status === 'fulfilled' ? pubs.value.data : [];
  ads.value = adsRes.status === 'fulfilled' ? adsRes.value.data : null;
  alerts.value = alertsRes.status === 'fulfilled' ? alertsRes.value.data.length : 0;
}

function refreshAll() {
  void load();
  void loadToday();
}

onMounted(refreshAll);
watch(() => brand.selected, refreshAll);
</script>

<style lang="scss" scoped>
.today-card {
  height: 100%; width: 100%; color: inherit; text-decoration: none; cursor: pointer; font: inherit;
  border: 1px solid rgba(0, 0, 0, 0.08); background: #fff;
  transition: transform 0.15s ease, border-color 0.15s ease, box-shadow 0.15s ease;
  &:hover, &:focus-visible { transform: translateY(-2px); border-color: rgba($primary, 0.5); box-shadow: 0 8px 24px rgba($primary, 0.12); outline: none; }
  &.attention { border-color: rgba($warning, 0.6); }
  &.danger { border-color: rgba($negative, 0.6); }
}
.body--dark .today-card { border-color: rgba(255, 255, 255, 0.08); background: $dark; }
.body--dark .today-card.attention { border-color: rgba($warning, 0.5); }
.body--dark .today-card.danger { border-color: rgba($negative, 0.5); }
</style>

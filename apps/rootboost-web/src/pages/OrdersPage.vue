<template>
  <q-page class="q-pa-lg">
    <div class="row items-end q-mb-lg">
      <div class="col">
        <div class="text-h5 sora-bold">{{ $t('pages.orders.title') }}</div>
        <div class="text-grey-7">{{ $t('pages.orders.subtitle') }}</div>
      </div>
      <q-btn flat round :icon="mdiRefresh" :loading="loading" @click="load">
        <q-tooltip>{{ $t('common.actions.refresh') }}</q-tooltip>
      </q-btn>
    </div>

    <q-banner v-if="error" rounded class="bg-red-1 text-negative q-mb-md">
      {{ $t('common.error') }}
      <template #action><q-btn flat :label="$t('common.actions.retry')" @click="load" /></template>
    </q-banner>

    <q-table flat bordered class="card-rounded" :rows="orders" :columns="columns" row-key="paymentId"
      :loading="loading" :no-data-label="$t('common.empty')" :rows-per-page-options="[25, 50, 100]">
      <template #body-cell-status="props">
        <q-td :props="props">
          <q-chip dense square :color="statusColor(props.row.status)" text-color="white">{{ props.row.status }}</q-chip>
          <q-chip v-if="props.row.isTest" dense square outline color="grey-6">
            {{ $t('pages.orders.test') }}
            <q-tooltip>{{ $t('pages.orders.testHint') }}</q-tooltip>
          </q-chip>
          <q-icon v-if="props.row.failureReason" :name="mdiAlertCircleOutline" color="negative" class="q-ml-xs">
            <q-tooltip>{{ props.row.failureReason }}</q-tooltip>
          </q-icon>
        </q-td>
      </template>
    </q-table>
  </q-page>
</template>

<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import type { QTableColumn } from 'quasar';
import { mdiRefresh, mdiAlertCircleOutline } from '@quasar/extras/mdi-v7';
import { api } from 'src/services/api';
import type { Order } from 'src/types/api';
import { formatDateTime, formatMoney } from 'src/utils/format';
import { statusColor } from 'src/utils/orderStatus';
import { brandParams, useBrandStore } from 'src/stores/brandStore';

defineOptions({ name: 'OrdersPage' });

const { t } = useI18n();
const orders = ref<Order[]>([]);
const loading = ref(false);
const error = ref(false);
const brand = useBrandStore();

async function load() {
  loading.value = true;
  error.value = false;
  try {
    const { data } = await api.get<Order[]>('/orders', { params: brandParams(brand.selected) });
    orders.value = data;
  } catch {
    error.value = true;
  } finally {
    loading.value = false;
  }
}

const columns = computed<QTableColumn<Order>[]>(() => [
  { name: 'createdAt', label: t('pages.orders.date'), field: 'createdAt', format: (v: string) => formatDateTime(v), align: 'left', sortable: true },
  { name: 'customer', label: t('pages.orders.customer'), field: (r) => `${r.shipName} (${r.shipCountry})`, align: 'left' },
  { name: 'product', label: t('pages.orders.product'), field: (r) => `${r.productKey} x${r.quantity}`, align: 'left' },
  { name: 'amount', label: t('pages.orders.amount'), field: (r) => formatMoney(r.amount, r.currency), align: 'right' },
  { name: 'status', label: t('pages.orders.status'), field: 'status', align: 'left', sortable: true },
  { name: 'supplier', label: t('pages.orders.supplier'), field: (r) => r.supplierOrderId ?? '', align: 'left' },
  { name: 'tracking', label: t('pages.orders.tracking'), field: (r) => r.tracking ?? '', align: 'left' },
  { name: 'brand', label: t('pages.orders.brand'), field: (r) => brand.brands.find((b) => b.key === r.brandKey)?.name ?? r.brandKey, align: 'left' },
]);

onMounted(load);
watch(() => brand.selected, load);
</script>

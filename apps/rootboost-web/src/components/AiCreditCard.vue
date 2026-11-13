<template>
  <!-- Crédito da IA (13/11, uso interno): gasto do mês contra o teto. Passou do teto, a IA para com aviso claro. -->
  <q-card v-if="status" flat bordered class="card-rounded q-pa-md q-mb-xl">
    <div class="row items-center q-gutter-sm q-mb-sm">
      <q-icon :name="mdiCreditCardOutline" color="primary" size="22px" />
      <div class="col">
        <div class="text-subtitle1 text-weight-bold">{{ $t('aiCredit.title') }}</div>
        <div class="text-caption text-grey-7">{{ $t('aiCredit.subtitle', { month: monthName }) }}</div>
      </div>
      <q-btn flat dense no-caps color="primary" :label="$t('aiCredit.setLimit')" @click="openEdit" />
    </div>

    <div class="row items-end q-gutter-x-md q-mb-sm">
      <div class="text-h5 sora-bold">{{ usd(status.spentUsd) }}</div>
      <div class="text-grey-7 q-pb-xs">
        <template v-if="status.limitUsd !== null">{{ $t('aiCredit.ofLimit', { limit: usd(status.limitUsd) }) }}</template>
        <template v-else>{{ $t('aiCredit.noLimit') }}</template>
      </div>
    </div>
    <q-linear-progress v-if="status.limitUsd !== null" :value="Math.min((status.percent ?? 0) / 100, 1)" rounded size="10px"
      :color="barColor" track-color="grey-3" class="q-mb-sm" />
    <q-banner v-if="status.blocked" rounded dense class="bg-negative text-white q-mb-sm">{{ $t('aiCredit.blocked') }}</q-banner>

    <div class="row q-col-gutter-sm text-caption text-grey-8">
      <div class="col-12 col-sm-4">{{ $t('aiCredit.today', { v: usd(status.todayUsd) }) }}</div>
      <div class="col-12 col-sm-4">{{ $t('aiCredit.projected', { v: usd(status.projectedUsd) }) }}</div>
      <div class="col-12 col-sm-4">{{ $t('aiCredit.calls', { n: status.calls }) }}</div>
    </div>
    <div v-if="status.byPurpose.length" class="q-mt-sm">
      <div v-for="p in status.byPurpose.slice(0, 4)" :key="p.key" class="row items-center text-caption q-py-xs">
        <div class="col">{{ purpose(p.key) }}</div>
        <div class="text-grey-7">{{ usd(p.costUsd) }} · {{ p.calls }}</div>
      </div>
    </div>
    <div class="text-caption text-grey-6 q-mt-sm">{{ $t('aiCredit.note') }}</div>

    <q-dialog v-model="editing">
      <q-card style="width: 420px; max-width: 94vw" class="q-pa-sm">
        <q-card-section class="text-h6">{{ $t('aiCredit.setLimit') }}</q-card-section>
        <q-card-section class="column q-gutter-y-md">
          <q-input v-model.number="form.limit" type="number" min="0" step="1" outlined dense prefix="US$" :label="$t('aiCredit.limitLabel')"
            :hint="$t('aiCredit.limitHint')" />
          <q-input v-model.number="form.alert" type="number" min="10" max="100" outlined dense suffix="%" :label="$t('aiCredit.alertLabel')" />
        </q-card-section>
        <q-card-actions align="right">
          <q-btn flat no-caps :label="$t('aiCredit.cancel')" v-close-popup />
          <q-btn unelevated no-caps color="primary" :loading="saving" :label="$t('aiCredit.save')" @click="save" />
        </q-card-actions>
      </q-card>
    </q-dialog>
  </q-card>
</template>

<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import { useQuasar } from 'quasar';
import { isAxiosError } from 'axios';
import { mdiCreditCardOutline } from '@quasar/extras/mdi-v7';
import { api } from 'src/services/api';
import { formatMoney } from 'src/utils/format';

defineOptions({ name: 'AiCreditCard' });

interface Line { key: string; costUsd: number; calls: number }
interface Status {
  monthStart: string; spentUsd: number; todayUsd: number; calls: number; limitUsd: number | null; alertPercent: number;
  percent: number | null; projectedUsd: number; blocked: boolean; byPurpose: Line[]; byBrand: Line[];
}

const i18n = useI18n();
const { t, locale } = i18n;
const $q = useQuasar();
const status = ref<Status | null>(null);
const editing = ref(false);
const saving = ref(false);
const form = reactive<{ limit: number | null; alert: number }>({ limit: null, alert: 80 });

const usd = (v: number) => formatMoney(v, 'USD');
const purpose = (k: string) => (i18n.te(`aiCredit.purpose.${k}`) ? t(`aiCredit.purpose.${k}`) : k);
const monthName = computed(() => status.value ? new Date(status.value.monthStart).toLocaleDateString(locale.value, { month: 'long', timeZone: 'UTC' }) : '');
const barColor = computed(() => {
  const p = status.value?.percent ?? 0;
  return p >= 100 ? 'negative' : p >= (status.value?.alertPercent ?? 80) ? 'warning' : 'primary';
});

async function load() {
  try { status.value = (await api.get<Status>('/admin/ai/credit')).data; } catch { status.value = null; }
}

function openEdit() {
  form.limit = status.value?.limitUsd ?? null;
  form.alert = status.value?.alertPercent ?? 80;
  editing.value = true;
}

async function save() {
  saving.value = true;
  try {
    const limit = form.limit === null || String(form.limit) === '' ? null : Number(form.limit);
    status.value = (await api.put<Status>('/admin/ai/credit', { monthlyLimitUsd: limit, alertPercent: Number(form.alert) || 80 })).data;
    editing.value = false;
    $q.notify({ type: 'positive', message: t('aiCredit.saved') });
  } catch (e) {
    const msg = isAxiosError(e) && typeof e.response?.data?.error === 'string' ? e.response.data.error : t('aiCredit.error');
    $q.notify({ type: 'negative', message: msg });
  } finally { saving.value = false; }
}

onMounted(() => void load());
</script>

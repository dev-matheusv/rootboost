<template>
  <!-- Molde do menu do MedlyCare: fixo no desktop, sobreposto e fechado no celular. -->
  <q-drawer v-model="open" show-if-above side="left" bordered :mini="isMini" :width="248" :mini-width="64"
    :class="$q.dark.isActive ? 'bg-dark' : 'bg-white'">
    <div class="column full-height no-wrap">
      <div class="row items-center no-wrap q-pa-md" style="height: 65px">
        <q-btn flat round dense :icon="mdiMenu" @click="isMini = !isMini" />
        <div v-if="!isMini" class="row items-center no-wrap q-ml-sm">
          <img src="/favicon.svg" alt="" width="26" height="26" />
          <div class="text-h6 sora-bold q-ml-sm">RootBoost</div>
        </div>
      </div>

      <div class="q-px-sm">
        <q-item clickable rounded :class="$q.dark.isActive ? 'dark-focus' : 'light-focus'" @click="passwordOpen = true">
          <q-tooltip v-if="isMini" anchor="center right" self="center left">{{ $t('password.title') }}</q-tooltip>
          <q-item-section avatar><q-icon :name="mdiAccountCircleOutline" /></q-item-section>
          <q-item-section v-if="!isMini">
            <q-item-label class="montserrat-bold ellipsis">{{ auth.userName }}</q-item-label>
            <q-item-label caption class="ellipsis">{{ auth.userEmail }}</q-item-label>
          </q-item-section>
        </q-item>
      </div>

      <!-- Marca escolhida filtra todas as telas (pedidos, visão geral e o que vier depois). -->
      <div v-if="!isMini" class="q-px-sm q-pt-sm">
        <q-select :model-value="brand.selected" :options="brandOptions" emit-value map-options dense outlined
          :label="$t('nav.brand')" @update:model-value="(key: string) => brand.select(key)">
          <template #prepend><q-icon :name="mdiStorefrontOutline" size="xs" /></template>
        </q-select>
        <!-- Demonstração: selo visível e restaurar. Nada desta marca publica nas redes. -->
        <div v-if="isDemo" class="demo row items-center no-wrap q-mt-xs q-px-sm q-py-xs">
          <q-icon :name="mdiTestTube" size="16px" class="q-mr-xs" />
          <div class="col text-caption">{{ $t('demo.badge') }}</div>
          <q-btn flat dense round size="sm" :icon="mdiRestore" :loading="resetting" @click="resetDemo">
            <q-tooltip>{{ $t('demo.reset') }}</q-tooltip>
          </q-btn>
        </div>
      </div>

      <q-list padding class="col q-px-sm q-gutter-y-xs" style="overflow-y: auto">
        <q-item v-for="item in items" :key="item.name" clickable v-ripple rounded
          :active="route.name === item.name" active-class="text-primary light-active" :to="{ name: item.name }">
          <q-item-section avatar><q-icon :name="item.icon" /></q-item-section>
          <q-item-section v-if="!isMini">{{ $t(item.label) }}</q-item-section>
          <q-tooltip v-if="isMini" anchor="center right" self="center left">{{ $t(item.label) }}</q-tooltip>
        </q-item>
      </q-list>

      <q-list padding class="q-px-sm">
        <!-- Busca rápida: o mesmo que Ctrl+K, pra quem não lembra do atalho. -->
        <q-item clickable v-ripple rounded @click="emit('search')">
          <q-item-section avatar><q-icon :name="mdiMagnify" /></q-item-section>
          <q-item-section v-if="!isMini">{{ $t('palette.open') }}</q-item-section>
          <q-item-section v-if="!isMini" side><span class="kbd">Ctrl K</span></q-item-section>
          <q-tooltip v-if="isMini" anchor="center right" self="center left">{{ $t('palette.open') }} (Ctrl K)</q-tooltip>
        </q-item>
        <!-- Avisos de operação (RB-58): post que falhou, sync de anúncio, conexão da Meta. -->
        <q-item clickable v-ripple rounded :class="{ 'text-negative': alerts.length > 0 }" @click="alertsOpen = true">
          <q-item-section avatar>
            <q-icon :name="alerts.length > 0 ? mdiBellAlertOutline : mdiBellOutline">
              <q-badge v-if="badge" color="negative" floating rounded>{{ badge }}</q-badge>
            </q-icon>
          </q-item-section>
          <q-item-section v-if="!isMini">{{ $t('alerts.title') }}</q-item-section>
          <q-tooltip v-if="isMini" anchor="center right" self="center left">{{ $t('alerts.title') }}</q-tooltip>
        </q-item>
        <PushToggle :mini="isMini" />
        <q-item clickable v-ripple rounded @click="$q.dark.toggle()">
          <q-item-section avatar><q-icon :name="$q.dark.isActive ? mdiWeatherSunny : mdiWeatherNight" /></q-item-section>
          <q-item-section v-if="!isMini">{{ $t('nav.darkMode') }}</q-item-section>
        </q-item>
        <q-item clickable v-ripple rounded @click="logout">
          <q-item-section avatar><q-icon :name="mdiLogout" /></q-item-section>
          <q-item-section v-if="!isMini">{{ $t('common.actions.logout') }}</q-item-section>
        </q-item>
      </q-list>
    </div>
    <ChangePasswordDialog v-model="passwordOpen" />
    <AlertsDialog v-model="alertsOpen" v-model:alerts="alerts" @changed="loadAlerts" />
  </q-drawer>
</template>

<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import { useRoute, useRouter } from 'vue-router';
import {
  mdiAccountCircleOutline, mdiBellAlertOutline, mdiBellOutline, mdiBullhornOutline, mdiCalendarMonthOutline, mdiChartBar, mdiImageMultipleOutline, mdiLogout, mdiMagnify, mdiMenu, mdiMicrophoneOutline, mdiMovieOpenPlayOutline, mdiPackageVariantClosed, mdiRestore, mdiStorefrontOutline, mdiTestTube, mdiViewDashboardOutline, mdiWeatherNight, mdiWeatherSunny,
} from '@quasar/extras/mdi-v7';
import { api } from 'src/services/api';
import { useQuasar } from 'quasar';
import type { OpsAlert } from 'src/types/api';
import { badgeText } from 'src/utils/alerts';
import { OPEN_ALERTS_EVENT } from 'src/utils/overview';
import AlertsDialog from 'src/components/AlertsDialog.vue';
import { useAuthStore } from 'src/stores/authStore';
import { useBrandStore } from 'src/stores/brandStore';
import ChangePasswordDialog from 'src/components/ChangePasswordDialog.vue';
import PushToggle from 'src/components/PushToggle.vue';

defineOptions({ name: 'SidebarNavigation' });

const open = defineModel<boolean>({ default: false });
const emit = defineEmits<{ search: [] }>();
const isMini = ref(false);
const passwordOpen = ref(false);
const auth = useAuthStore();
const brand = useBrandStore();
const route = useRoute();
const router = useRouter();
const { t } = useI18n();

const brandOptions = computed(() => [
  { label: t('nav.allBrands'), value: '' },
  ...brand.brands.map((b) => ({ label: b.name, value: b.key })),
]);

const alertsOpen = ref(false);
const alerts = ref<OpsAlert[]>([]);
const badge = computed(() => badgeText(alerts.value));
let alertsTimer: ReturnType<typeof setInterval> | undefined;

/** Falha ao buscar avisos não atrapalha o Console: mantém a lista anterior. */
async function loadAlerts() {
  try {
    alerts.value = (await api.get<OpsAlert[]>('/admin/alerts')).data;
  } catch {
    // sem rede ou sessão vencida: a próxima rodada tenta de novo
  }
}

// A Visão geral (e outras telas) abrem os avisos por evento: o diálogo mora aqui no menu.
const showAlerts = () => (alertsOpen.value = true);

onMounted(() => {
  window.addEventListener(OPEN_ALERTS_EVENT, showAlerts);
  // Tocar no aviso do celular abre o Console com ?avisos=1: já mostra a lista.
  if (route.query.avisos) {
    alertsOpen.value = true;
    void router.replace({ query: {} });
  }
  if (!brand.loaded) void brand.load().catch(() => undefined);
  void loadAlerts();
  alertsTimer = setInterval(() => void loadAlerts(), 2 * 60_000);
});

onUnmounted(() => {
  clearInterval(alertsTimer);
  window.removeEventListener(OPEN_ALERTS_EVENT, showAlerts);
});

// Cada fase do roadmap acrescenta itens aqui (Campanhas, Criativos, Agenda, IA).
const items = [
  { name: 'overview', label: 'nav.overview', icon: mdiViewDashboardOutline },
  { name: 'orders', label: 'nav.orders', icon: mdiPackageVariantClosed },
  { name: 'campaigns', label: 'nav.campaigns', icon: mdiChartBar },
  { name: 'publications', label: 'nav.publications', icon: mdiCalendarMonthOutline },
  { name: 'studio', label: 'nav.studio', icon: mdiMovieOpenPlayOutline },
  { name: 'acervo', label: 'nav.acervo', icon: mdiImageMultipleOutline },
  { name: 'vozes', label: 'nav.vozes', icon: mdiMicrophoneOutline },
  { name: 'brandProfile', label: 'nav.brandProfile', icon: mdiBullhornOutline },
];

async function logout() {
  auth.logout();
  await router.replace({ name: 'login' });
}

// --- Marca de demonstração (chave "-demo"): selo e restaurar ---
const $q = useQuasar();
const isDemo = computed(() => brand.selected.endsWith('-demo'));
const resetting = ref(false);
function resetDemo() {
  $q.dialog({ title: t('demo.reset'), message: t('demo.resetConfirm'), cancel: true }).onOk(() => {
    resetting.value = true;
    void api.post(`/admin/brands/${brand.selected}/demo/reset`)
      .then(() => { $q.notify({ type: 'positive', message: t('demo.resetDone') }); window.location.reload(); })
      .catch(() => $q.notify({ type: 'negative', message: t('common.error') }))
      .finally(() => (resetting.value = false));
  });
}
</script>

<style lang="scss" scoped>
.light-active { background: rgba($primary, 0.1); }
.kbd { font-size: 11px; padding: 1px 6px; border-radius: 6px; border: 1px solid rgba(128, 128, 128, 0.35); }
.demo { border-radius: 8px; background: rgba(255, 122, 89, 0.15); color: #ff7a59; }
</style>

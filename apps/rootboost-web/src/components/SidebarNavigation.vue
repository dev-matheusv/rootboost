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
        <q-item rounded :class="$q.dark.isActive ? 'dark-focus' : 'light-focus'">
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
  </q-drawer>
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import { useRoute, useRouter } from 'vue-router';
import {
  mdiMenu, mdiAccountCircleOutline, mdiViewDashboardOutline, mdiPackageVariantClosed,
  mdiWeatherNight, mdiWeatherSunny, mdiLogout, mdiStorefrontOutline, mdiBullhornOutline,
} from '@quasar/extras/mdi-v7';
import { useAuthStore } from 'src/stores/authStore';
import { useBrandStore } from 'src/stores/brandStore';

defineOptions({ name: 'SidebarNavigation' });

const open = defineModel<boolean>({ default: false });
const isMini = ref(false);
const auth = useAuthStore();
const brand = useBrandStore();
const route = useRoute();
const router = useRouter();
const { t } = useI18n();

const brandOptions = computed(() => [
  { label: t('nav.allBrands'), value: '' },
  ...brand.brands.map((b) => ({ label: b.name, value: b.key })),
]);

onMounted(() => {
  if (!brand.loaded) void brand.load().catch(() => undefined);
});

// Cada fase do roadmap acrescenta itens aqui (Campanhas, Criativos, Agenda, IA).
const items = [
  { name: 'overview', label: 'nav.overview', icon: mdiViewDashboardOutline },
  { name: 'orders', label: 'nav.orders', icon: mdiPackageVariantClosed },
  { name: 'brandProfile', label: 'nav.brandProfile', icon: mdiBullhornOutline },
];

async function logout() {
  auth.logout();
  await router.replace({ name: 'login' });
}
</script>

<style lang="scss" scoped>
.light-active { background: rgba(79, 70, 229, 0.1); }
</style>

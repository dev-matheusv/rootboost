<template>
  <q-layout view="lHh Lpr lFf">
    <q-header v-if="$q.screen.lt.md" bordered :class="$q.dark.isActive ? 'bg-dark' : 'bg-white text-dark'">
      <q-toolbar>
        <q-btn flat round dense :icon="mdiMenu" @click="drawer = !drawer" />
        <q-toolbar-title class="sora-bold">RootBoost</q-toolbar-title>
        <q-btn-dropdown v-if="brand.brands.length" flat dense no-caps class="brand-pick" :label="brand.selectedName || $t('nav.allBrands')">
          <q-list dense>
            <q-item v-for="b in [{ key: '', name: $t('nav.allBrands') }, ...brand.brands]" :key="b.key" v-close-popup clickable
              :active="b.key === brand.selected" @click="brand.select(b.key)">
              <q-item-section>{{ b.name }}</q-item-section>
            </q-item>
          </q-list>
        </q-btn-dropdown>
        <q-btn flat round dense :icon="mdiMagnify" @click="palette = true" />
      </q-toolbar>
    </q-header>

    <SidebarNavigation v-model="drawer" @search="palette = true" />
    <CommandPalette v-model="palette" />

    <q-page-container>
      <router-view v-slot="{ Component }">
        <transition name="fade" mode="out-in">
          <component :is="Component" />
        </transition>
      </router-view>
    </q-page-container>
  </q-layout>
</template>

<script setup lang="ts">
import { ref } from 'vue';
import { mdiMagnify, mdiMenu } from '@quasar/extras/mdi-v7';
import SidebarNavigation from 'components/SidebarNavigation.vue';
import CommandPalette from 'components/CommandPalette.vue';
import { useBrandStore } from 'src/stores/brandStore';

defineOptions({ name: 'MainLayout' });

const drawer = ref(false);
const palette = ref(false);
const brand = useBrandStore();
</script>

<style scoped>
.brand-pick { max-width: 45vw; }
.brand-pick :deep(.q-btn__content) { flex-wrap: nowrap; }
.brand-pick :deep(.block) { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
</style>

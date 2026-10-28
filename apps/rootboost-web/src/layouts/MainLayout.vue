<template>
  <q-layout view="lHh Lpr lFf">
    <q-header v-if="$q.screen.lt.md" bordered :class="$q.dark.isActive ? 'bg-dark' : 'bg-white text-dark'">
      <q-toolbar>
        <q-btn flat round dense :icon="mdiMenu" @click="drawer = !drawer" />
        <q-toolbar-title class="sora-bold">RootBoost</q-toolbar-title>
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

defineOptions({ name: 'MainLayout' });

const drawer = ref(false);
const palette = ref(false);
</script>

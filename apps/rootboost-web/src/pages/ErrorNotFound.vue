<template>
  <div class="fullscreen flex flex-center column text-center q-pa-md">
    <div class="sora-bold" style="font-size: 6rem">404</div>
    <div class="text-h6 q-mb-lg">{{ $t('pages.notFound') }}</div>
    <q-btn unelevated rounded color="primary" to="/" :label="$t('nav.overview')" />
  </div>
</template>

<script setup lang="ts">
defineOptions({ name: 'ErrorNotFound' });
</script>

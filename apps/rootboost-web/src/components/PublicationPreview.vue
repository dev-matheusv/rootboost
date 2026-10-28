<template>
  <!-- Prévia do post como sai no celular, com um canal por aba e as ações do grupo (aprovar todos de uma vez). -->
  <div class="preview column q-gutter-y-md">
    <template v-if="bundle && current">
      <q-btn-toggle :model-value="current.id" spread no-caps unelevated rounded toggle-color="primary"
        :options="tabs" class="preview-tabs" @update:model-value="(id: string) => emit('select', id)">
        <template v-for="p in bundle.items" #[p.id] :key="p.id">
          <span class="row items-center no-wrap q-gutter-x-xs">
            <span class="dot" :class="`bg-${statusColor(p.status)}`" />
            <span>{{ p.channel }}</span>
          </span>
        </template>
      </q-btn-toggle>

      <div class="phone self-center">
        <div class="screen">
          <video v-if="isVideo(current.mediaUrl)" :key="current.mediaUrl" :src="current.mediaUrl" :poster="current.coverUrl ?? undefined"
            controls playsinline muted preload="metadata" class="media" />
          <img v-else :src="current.mediaUrl" alt="" class="media" />
          <div class="overlay" :class="{ 'above-controls': isVideo(current.mediaUrl) }">
            <div class="row items-center no-wrap q-gutter-x-xs text-weight-bold">
              <span class="avatar" /><span>{{ brandKey }}</span>
            </div>
            <div class="ellipsis-2-lines">{{ current.caption }}</div>
          </div>
        </div>
      </div>

      <div class="row items-center q-gutter-sm">
        <q-chip dense square :color="statusColor(current.status)" text-color="white" class="q-ma-none">
          {{ $t(`pages.publications.status.${current.status}`) }}
        </q-chip>
        <span class="text-caption text-grey-7">{{ when }}</span>
        <q-space />
        <a v-if="current.publishedUrl" :href="current.publishedUrl" target="_blank" rel="noopener" class="text-caption text-primary">
          {{ $t('pages.publications.openPost') }}
        </a>
      </div>
      <q-banner v-if="current.error" dense rounded class="bg-red-1 text-negative">
        {{ current.error }}
        <template v-if="current.status === 'Failed' && current.approvedBy" #action>
          <q-btn flat dense no-caps color="negative" :icon="mdiRefresh" :label="$t('pages.publications.retry')" @click="emit('retry', current)" />
        </template>
      </q-banner>
      <div class="caption-full text-body2">{{ current.caption }}</div>

      <div v-if="current.channel === 'LinkedIn' && current.status === 'Approved'" class="row q-gutter-sm">
        <q-btn outline no-caps color="primary" :label="$t('pages.publications.download')" :href="current.mediaUrl" target="_blank" />
        <q-btn outline no-caps color="primary" :label="$t('pages.publications.copyCaption')" @click="emit('copy', current)" />
        <q-btn outline no-caps color="positive" :label="$t('pages.publications.markPublished')" @click="emit('markPublished', current)" />
      </div>

      <div class="row q-gutter-sm no-wrap">
        <q-btn v-if="canEdit(current)" outline no-caps no-wrap class="col-auto" :icon="mdiPencilOutline" :label="$t('pages.publications.edit')"
          @click="emit('edit', current)" />
        <q-btn v-if="drafts.length" unelevated no-caps no-wrap color="primary" class="col" :loading="busy" :icon="mdiCheckAll"
          :label="drafts.length > 1 ? $t('pages.publications.approveAll', { n: drafts.length }) : $t('pages.publications.approve')"
          @click="emit('approve', bundle)" />
      </div>
      <div class="text-caption text-grey-6 text-center gt-sm">{{ $t('pages.publications.shortcuts') }}</div>
    </template>

    <div v-else class="column items-center q-pa-xl text-grey-6 text-center">
      <q-icon :name="mdiCellphoneScreenshot" size="48px" class="q-mb-sm" />
      {{ $t('pages.publications.pickPost') }}
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';
import { mdiCellphoneScreenshot, mdiCheckAll, mdiPencilOutline, mdiRefresh } from '@quasar/extras/mdi-v7';
import type { Publication } from 'src/types/api';
import { canEdit, isVideo, statusColor, type PostBundle } from 'src/utils/publications';

defineOptions({ name: 'PublicationPreview' });

const props = defineProps<{ bundle: PostBundle | null; selectedId: string | null; brandKey: string; busy: boolean }>();
const emit = defineEmits<{
  select: [id: string];
  approve: [bundle: PostBundle];
  edit: [p: Publication];
  copy: [p: Publication];
  markPublished: [p: Publication];
  retry: [p: Publication];
}>();

const { locale } = useI18n();

const current = computed(() => props.bundle?.items.find((p) => p.id === props.selectedId) ?? props.bundle?.items[0] ?? null);
const drafts = computed(() => props.bundle?.items.filter((p) => p.status === 'Draft') ?? []);
const tabs = computed(() => props.bundle?.items.map((p) => ({ value: p.id, slot: p.id })) ?? []);
const when = computed(() =>
  current.value
    ? new Date(current.value.scheduledFor).toLocaleString(locale.value, { weekday: 'short', day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit' })
    : '',
);
</script>

<style lang="scss" scoped>
.preview { min-width: 0; }
.preview-tabs { border: 1px solid rgba(0, 0, 0, 0.08); }
.body--dark .preview-tabs { border-color: rgba(255, 255, 255, 0.08); }
.dot { width: 8px; height: 8px; border-radius: 50%; display: inline-block; box-shadow: 0 0 0 1.5px rgba(255, 255, 255, 0.7); }
.phone { width: 260px; padding: 7px; border-radius: 34px; background: #e4dff5; }
.body--dark .phone { background: $dark-focus; }
.screen { position: relative; height: 400px; border-radius: 28px; overflow: hidden; background: #05040b; }
.media { width: 100%; height: 100%; object-fit: cover; display: block; }
.overlay {
  position: absolute; left: 0; right: 0; bottom: 0; padding: 40px 14px 14px; color: #fff; font-size: 12px; line-height: 1.4;
  background: linear-gradient(transparent, rgba(0, 0, 0, 0.65)); pointer-events: none;
}
.overlay.above-controls { bottom: 44px; background: linear-gradient(transparent, rgba(0, 0, 0, 0.45)); }
.avatar { width: 20px; height: 20px; border-radius: 50%; background: $primary; display: inline-block; }
.caption-full { white-space: pre-line; max-height: 180px; overflow-y: auto; }
</style>

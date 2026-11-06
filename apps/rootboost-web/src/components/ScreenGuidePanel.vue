<template>
  <!-- Guia de uso do sistema (RB-50): a IA lê a documentação e propõe; a pessoa confere e salva. A gravação consulta este texto. -->
  <div class="guide card-rounded q-pa-md q-mt-lg column q-gutter-y-sm">
    <div class="text-subtitle1 text-weight-bold">{{ $t('screenGuide.title') }}</div>
    <div class="text-caption text-grey-7">{{ $t('screenGuide.hint') }}</div>
    <q-input v-model="documentation" outlined dense autogrow type="textarea" maxlength="40000" :label="$t('screenGuide.documentation')" />
    <div>
      <q-btn outline color="primary" no-caps :label="$t('screenGuide.generate')" :loading="drafting"
        :disable="!documentation.trim()" @click="draft" />
    </div>
    <q-input v-model="guide" outlined dense autogrow type="textarea" maxlength="12000" :label="$t('screenGuide.guide')" :hint="$t('screenGuide.guideHint')" />
    <div class="row items-center q-gutter-sm">
      <q-btn unelevated color="primary" no-caps :label="$t('screenGuide.save')" :loading="saving" @click="save" />
      <q-chip v-if="saved" dense color="positive" text-color="white">{{ $t('screenGuide.saved') }}</q-chip>
    </div>
  </div>
</template>

<script setup lang="ts">
import { onMounted, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import { useQuasar } from 'quasar';
import { isAxiosError } from 'axios';
import { api } from 'src/services/api';

defineOptions({ name: 'ScreenGuidePanel' });

const props = defineProps<{ brandKey: string }>();
const { t } = useI18n();
const $q = useQuasar();

const documentation = ref('');
const guide = ref('');
const drafting = ref(false);
const saving = ref(false);
const saved = ref(false);

function fail(e: unknown) {
  const msg = isAxiosError(e) && typeof e.response?.data?.error === 'string' ? e.response.data.error : t('common.saveError');
  $q.notify({ type: 'negative', message: msg });
}

async function load() {
  const { data, status } = await api.get<{ screenGuide?: string | null }>(`/admin/brands/${props.brandKey}/product`);
  guide.value = status === 200 ? data.screenGuide ?? '' : '';
}

async function draft() {
  drafting.value = true;
  try {
    const { data } = await api.post<{ guide: string }>(`/admin/brands/${props.brandKey}/product/screen-guide/draft`,
      { documentation: documentation.value }, { timeout: 120_000 });
    guide.value = data.guide;
    saved.value = false;
  } catch (e) {
    fail(e);
  } finally {
    drafting.value = false;
  }
}

async function save() {
  saving.value = true;
  try {
    await api.put(`/admin/brands/${props.brandKey}/product/screen-guide`, { guide: guide.value });
    saved.value = true;
  } catch (e) {
    fail(e);
  } finally {
    saving.value = false;
  }
}

watch(guide, () => { saved.value = false; });
onMounted(() => { void load().catch(() => { guide.value = ''; }); });
</script>

<style lang="scss" scoped>
.guide { border: 1px solid rgba(127, 127, 127, 0.25); }
</style>

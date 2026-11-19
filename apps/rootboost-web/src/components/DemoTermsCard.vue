<template>
  <!-- Termo da conta de demonstração (19/11): a pessoa confirma que só tem dado fictício e a gravação segue sem parar por dado pessoal. -->
  <q-banner v-if="terms && !terms.confirmed" rounded class="bg-amber-1 text-brown-10 q-mb-md">
    <template #avatar><q-icon :name="mdiShieldAccountOutline" color="amber-9" /></template>
    <div class="text-weight-bold">{{ $t('demoTerms.title') }}</div>
    <div class="text-caption q-mb-sm">{{ $t('demoTerms.why') }}</div>
    <q-checkbox v-model="agree" dense :label="terms.terms" class="text-body2" />
    <template #action>
      <q-btn unelevated color="primary" no-caps :disable="!agree" :loading="busy" :label="$t('demoTerms.confirm')" @click="confirm" />
    </template>
  </q-banner>
  <div v-else-if="terms?.confirmed" class="row items-center q-gutter-xs text-caption text-grey-7 q-mb-md">
    <q-icon :name="mdiShieldCheckOutline" color="positive" size="18px" />
    <span>{{ $t('demoTerms.confirmed', { who: terms.confirmedBy, when: when(terms.confirmedAt) }) }}</span>
    <q-btn flat dense no-caps size="sm" color="grey-7" :label="$t('demoTerms.undo')" :loading="busy" @click="undo" />
  </div>
</template>

<script setup lang="ts">
import { onMounted, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import { useQuasar } from 'quasar';
import { mdiShieldAccountOutline, mdiShieldCheckOutline } from '@quasar/extras/mdi-v7';
import { api } from 'src/services/api';

defineOptions({ name: 'DemoTermsCard' });

interface DemoTerms { confirmed: boolean; confirmedBy: string | null; confirmedAt: string | null; terms: string }

const props = defineProps<{ brandKey: string }>();
const { t, locale } = useI18n();
const $q = useQuasar();
const terms = ref<DemoTerms | null>(null);
const agree = ref(false);
const busy = ref(false);
const base = () => `/admin/brands/${props.brandKey}/demo-account`;

function when(iso: string | null) {
  return iso ? new Date(iso).toLocaleDateString(locale.value) : '';
}

async function load() {
  agree.value = false;
  try { terms.value = (await api.get<DemoTerms>(base())).data; } catch { terms.value = null; }
}

async function send(request: () => Promise<{ data: DemoTerms }>) {
  busy.value = true;
  try { terms.value = (await request()).data; } catch { $q.notify({ type: 'negative', message: t('demoTerms.error') }); } finally { busy.value = false; }
}

const confirm = () => send(() => api.post<DemoTerms>(`${base()}/confirm`));
const undo = () => send(() => api.delete<DemoTerms>(`${base()}/confirm`));

onMounted(load);
watch(() => props.brandKey, load);
</script>

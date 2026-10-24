<template>
  <q-page class="q-pa-lg">
    <div class="row items-end q-mb-lg">
      <div class="col">
        <div class="text-h5 sora-bold">{{ $t('pages.brandProfile.title') }}</div>
        <div class="text-grey-7">{{ profile ? profile.brand.name : $t('pages.brandProfile.subtitle') }}</div>
      </div>
      <q-btn v-if="brand.selected" flat round :icon="mdiRefresh" :loading="loading" @click="load">
        <q-tooltip>{{ $t('common.actions.refresh') }}</q-tooltip>
      </q-btn>
    </div>

    <!-- O perfil é sempre de uma marca: com "todas" escolhido, pede pra escolher. -->
    <q-banner v-if="!brand.selected" rounded class="bg-blue-1 text-primary">
      {{ $t('pages.brandProfile.pickBrand') }}
    </q-banner>

    <q-banner v-else-if="error" rounded class="bg-red-1 text-negative q-mb-md">
      {{ $t('common.error') }}
      <template #action><q-btn flat :label="$t('common.actions.retry')" @click="load" /></template>
    </q-banner>

    <template v-if="brand.selected && profile">
      <q-tabs v-model="tab" align="left" dense no-caps active-color="primary" indicator-color="primary" class="q-mb-md">
        <q-tab name="proof" :label="$t('pages.brandProfile.tabs.proof')" />
        <q-tab name="voice" :label="$t('pages.brandProfile.tabs.voice')" />
        <q-tab name="phrases" :label="$t('pages.brandProfile.tabs.phrases')" />
        <q-tab name="check" :label="$t('pages.brandProfile.tabs.check')" />
      </q-tabs>

      <q-tab-panels v-model="tab" class="bg-transparent">
        <!-- Inventário de prova: o que a IA pode afirmar. -->
        <q-tab-panel name="proof" class="q-pa-none">
          <div class="row items-center q-mb-sm">
            <div class="col text-grey-7">{{ $t('pages.brandProfile.proof.hint') }}</div>
            <q-btn unelevated color="primary" no-caps :icon="mdiPlus" :label="$t('pages.brandProfile.proof.add')" @click="openProof" />
          </div>
          <q-list bordered separator class="card-rounded">
            <q-item v-for="item in proofSorted" :key="item.id" :class="{ 'text-grey-6': !item.active }">
              <q-item-section>
                <q-item-label>
                  <q-chip dense square :color="proofStateColor(proofState(item))" text-color="white" class="q-ml-none">
                    {{ $t(`pages.brandProfile.proof.state.${proofState(item)}`) }}
                  </q-chip>
                  <q-chip dense square outline>{{ $t(`pages.brandProfile.proof.kinds.${item.kind}`) }}</q-chip>
                </q-item-label>
                <q-item-label class="q-mt-xs">{{ item.statement }}</q-item-label>
                <q-item-label caption>
                  {{ item.source }} · {{ $t('pages.brandProfile.proof.verified') }} {{ formatDate(item.verifiedAt) }}
                  <span v-if="item.reviewBy"> · {{ $t('pages.brandProfile.proof.reviewBy') }} {{ formatDate(item.reviewBy) }}</span>
                </q-item-label>
              </q-item-section>
              <q-item-section v-if="item.active" side>
                <q-btn flat round dense :icon="mdiArchiveOutline" @click="retire(item)">
                  <q-tooltip>{{ $t('pages.brandProfile.proof.retire') }}</q-tooltip>
                </q-btn>
              </q-item-section>
            </q-item>
            <q-item v-if="!profile.proof.length"><q-item-section class="text-grey-7">{{ $t('common.empty') }}</q-item-section></q-item>
          </q-list>
        </q-tab-panel>

        <!-- Voz: como a marca fala. -->
        <q-tab-panel name="voice" class="q-pa-none">
          <q-card flat bordered class="card-rounded q-pa-md">
            <q-input v-model="voice.audience" outlined :label="$t('pages.brandProfile.voice.audience')" class="q-mb-md" />
            <q-select v-model="voice.tone" outlined multiple use-chips use-input hide-dropdown-icon new-value-mode="add-unique"
              :label="$t('pages.brandProfile.voice.tone')" :hint="$t('pages.brandProfile.voice.enterHint')" class="q-mb-md" />
            <q-select v-model="voice.bannedTerms" outlined multiple use-chips use-input hide-dropdown-icon new-value-mode="add-unique"
              :label="$t('pages.brandProfile.voice.banned')" :hint="$t('pages.brandProfile.voice.enterHint')" class="q-mb-md" />

            <div class="text-subtitle2 q-mt-md">{{ $t('pages.brandProfile.voice.swaps') }}</div>
            <div v-for="(row, i) in swaps" :key="`s${i}`" class="row q-col-gutter-sm items-center q-mt-xs">
              <q-input v-model="row.from" dense outlined class="col" :label="$t('pages.brandProfile.voice.swapFrom')" />
              <q-input v-model="row.to" dense outlined class="col" :label="$t('pages.brandProfile.voice.swapTo')" />
              <div><q-btn flat round dense :icon="mdiClose" @click="swaps.splice(i, 1)" /></div>
            </div>
            <q-btn flat dense no-caps color="primary" :icon="mdiPlus" :label="$t('pages.brandProfile.voice.addSwap')" class="q-mt-xs"
              @click="swaps.push({ from: '', to: '' })" />

            <div class="text-subtitle2 q-mt-md">{{ $t('pages.brandProfile.voice.objections') }}</div>
            <div v-for="(row, i) in voice.objections" :key="`o${i}`" class="row q-col-gutter-sm items-center q-mt-xs">
              <q-input v-model="row.objection" dense outlined class="col-12 col-md-5" :label="$t('pages.brandProfile.voice.objection')" />
              <q-input v-model="row.answer" dense outlined autogrow class="col" :label="$t('pages.brandProfile.voice.answer')" />
              <div><q-btn flat round dense :icon="mdiClose" @click="voice.objections.splice(i, 1)" /></div>
            </div>
            <q-btn flat dense no-caps color="primary" :icon="mdiPlus" :label="$t('pages.brandProfile.voice.addObjection')" class="q-mt-xs"
              @click="voice.objections.push({ objection: '', answer: '' })" />

            <div class="row justify-end q-mt-lg">
              <q-btn unelevated color="primary" no-caps :label="$t('common.actions.save')" :loading="saving" @click="saveVoice" />
            </div>
          </q-card>
        </q-tab-panel>

        <!-- Frases reais de cliente: a matéria prima da copy humana. -->
        <q-tab-panel name="phrases" class="q-pa-none">
          <div class="row items-center q-mb-sm">
            <div class="col text-grey-7">{{ $t('pages.brandProfile.phrases.hint') }}</div>
            <q-btn unelevated color="primary" no-caps :icon="mdiPlus" :label="$t('pages.brandProfile.phrases.add')" @click="openPhrase" />
          </div>
          <q-list bordered separator class="card-rounded">
            <q-item v-for="p in profile.phrases" :key="p.id">
              <q-item-section>
                <q-item-label>“{{ p.text }}”</q-item-label>
                <q-item-label caption>{{ p.source }} · {{ formatDate(p.collectedAt) }}</q-item-label>
              </q-item-section>
            </q-item>
            <q-item v-if="!profile.phrases.length"><q-item-section class="text-grey-7">{{ $t('common.empty') }}</q-item-section></q-item>
          </q-list>
        </q-tab-panel>

        <!-- Checklist automática antes de qualquer peça chegar ao humano. -->
        <q-tab-panel name="check" class="q-pa-none">
          <q-card flat bordered class="card-rounded q-pa-md">
            <q-input v-model="checkText" outlined type="textarea" autogrow :label="$t('pages.brandProfile.check.text')" />
            <div class="row justify-end q-mt-sm">
              <q-btn unelevated color="primary" no-caps :label="$t('pages.brandProfile.check.run')" :loading="checking"
                :disable="!checkText.trim()" @click="runCheck" />
            </div>
          </q-card>
          <template v-if="check">
            <q-banner rounded :class="check.blocked ? 'bg-red-1 text-negative' : 'bg-green-1 text-positive'" class="q-mt-md">
              {{ check.blocked ? $t('pages.brandProfile.check.blocked') : $t('pages.brandProfile.check.ok') }}
            </q-banner>
            <q-list bordered separator class="card-rounded q-mt-sm">
              <q-item v-for="f in findings" :key="f.id">
                <q-item-section avatar><q-badge :color="findingColor(f)" rounded /></q-item-section>
                <q-item-section>
                  <q-item-label>
                    {{ f.message }}
                    <q-chip v-if="f.status === 'NeedsHuman' && f.blocking" dense square outline color="warning" class="q-ml-sm">
                      {{ $t('pages.brandProfile.check.mustConfirm') }}
                    </q-chip>
                  </q-item-label>
                  <q-item-label v-if="f.fix" caption>{{ f.fix }}</q-item-label>
                </q-item-section>
              </q-item>
            </q-list>
          </template>
        </q-tab-panel>
      </q-tab-panels>
    </template>

    <q-dialog v-model="proofDialog">
      <q-card style="width: 520px; max-width: 92vw" class="q-pa-sm">
        <q-card-section class="text-h6">{{ $t('pages.brandProfile.proof.add') }}</q-card-section>
        <q-card-section class="q-gutter-md">
          <q-select v-model="proofForm.kind" outlined emit-value map-options :options="kindOptions" :label="$t('pages.brandProfile.proof.kind')" />
          <q-input v-model="proofForm.statement" outlined autogrow :label="$t('pages.brandProfile.proof.statement')" />
          <q-input v-model="proofForm.source" outlined :label="$t('pages.brandProfile.proof.source')" :hint="$t('pages.brandProfile.proof.sourceHint')" />
          <div class="row q-col-gutter-sm">
            <q-input v-model="proofForm.verifiedAt" outlined type="date" class="col" stack-label :label="$t('pages.brandProfile.proof.verified')" />
            <q-input v-model="proofForm.reviewBy" outlined type="date" class="col" stack-label :label="$t('pages.brandProfile.proof.reviewBy')" />
          </div>
        </q-card-section>
        <q-card-actions align="right">
          <q-btn flat no-caps :label="$t('common.actions.cancel')" v-close-popup />
          <q-btn unelevated color="primary" no-caps :label="$t('common.actions.save')" :loading="saving"
            :disable="!proofForm.statement.trim() || !proofForm.source.trim()" @click="saveProof" />
        </q-card-actions>
      </q-card>
    </q-dialog>

    <q-dialog v-model="phraseDialog">
      <q-card style="width: 520px; max-width: 92vw" class="q-pa-sm">
        <q-card-section class="text-h6">{{ $t('pages.brandProfile.phrases.add') }}</q-card-section>
        <q-card-section class="q-gutter-md">
          <q-input v-model="phraseForm.text" outlined autogrow :label="$t('pages.brandProfile.phrases.text')" />
          <q-input v-model="phraseForm.source" outlined :label="$t('pages.brandProfile.proof.source')" :hint="$t('pages.brandProfile.phrases.sourceHint')" />
          <q-input v-model="phraseForm.collectedAt" outlined type="date" stack-label :label="$t('pages.brandProfile.phrases.collectedAt')" />
        </q-card-section>
        <q-card-actions align="right">
          <q-btn flat no-caps :label="$t('common.actions.cancel')" v-close-popup />
          <q-btn unelevated color="primary" no-caps :label="$t('common.actions.save')" :loading="saving"
            :disable="!phraseForm.text.trim() || !phraseForm.source.trim()" @click="savePhrase" />
        </q-card-actions>
      </q-card>
    </q-dialog>
  </q-page>
</template>

<script setup lang="ts">
import { computed, onMounted, reactive, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import { useQuasar } from 'quasar';
import { isAxiosError } from 'axios';
import { mdiRefresh, mdiPlus, mdiClose, mdiArchiveOutline } from '@quasar/extras/mdi-v7';
import { api } from 'src/services/api';
import type { BrandProfile, BrandVoice, CopyCheckResult, ProofItem, ProofKind } from 'src/types/api';
import { useBrandStore } from 'src/stores/brandStore';
import {
  PROOF_KINDS, cleanVoice, emptyVoice, findingColor, proofState, proofStateColor, sortFindings, sortProof,
} from 'src/utils/brandProfile';
import { formatDate } from 'src/utils/format';

defineOptions({ name: 'BrandProfilePage' });

const { t } = useI18n();
const $q = useQuasar();
const brand = useBrandStore();

const tab = ref('proof');
const profile = ref<BrandProfile | null>(null);
const loading = ref(false);
const saving = ref(false);
const error = ref(false);

const voice = reactive<BrandVoice>(emptyVoice());
const swaps = ref<{ from: string; to: string }[]>([]);

const base = computed(() => `/admin/brands/${brand.selected}`);
const proofSorted = computed(() => sortProof(profile.value?.proof ?? []));
const kindOptions = computed(() => PROOF_KINDS.map((k) => ({ value: k, label: t(`pages.brandProfile.proof.kinds.${k}`) })));

const today = () => new Date().toISOString().slice(0, 10);

function apiError(e: unknown): string {
  if (isAxiosError(e) && typeof e.response?.data?.error === 'string') return e.response.data.error;
  return t('common.saveError');
}

function notifyError(e: unknown) {
  $q.notify({ type: 'negative', message: apiError(e) });
}

async function load() {
  if (!brand.selected) {
    profile.value = null;
    return;
  }
  loading.value = true;
  error.value = false;
  try {
    const { data } = await api.get<BrandProfile>(`${base.value}/profile`);
    profile.value = data;
    const v = data.voice ?? emptyVoice();
    Object.assign(voice, {
      audience: v.audience ?? '',
      tone: [...(v.tone ?? [])],
      bannedTerms: [...(v.bannedTerms ?? [])],
      wordSwaps: { ...(v.wordSwaps ?? {}) },
      objections: (v.objections ?? []).map((o) => ({ ...o })),
    });
    swaps.value = Object.entries(v.wordSwaps ?? {}).map(([from, to]) => ({ from, to }));
  } catch {
    error.value = true;
  } finally {
    loading.value = false;
  }
}

async function saveVoice() {
  saving.value = true;
  try {
    const payload = cleanVoice({ ...voice, wordSwaps: Object.fromEntries(swaps.value.map((s) => [s.from, s.to])) });
    await api.put(`${base.value}/voice`, payload);
    $q.notify({ type: 'positive', message: t('common.saved') });
    await load();
  } catch (e) {
    notifyError(e);
  } finally {
    saving.value = false;
  }
}

const proofDialog = ref(false);
const proofForm = reactive<{ kind: ProofKind; statement: string; source: string; verifiedAt: string; reviewBy: string }>({
  kind: 'Feature', statement: '', source: '', verifiedAt: today(), reviewBy: '',
});

function openProof() {
  Object.assign(proofForm, { kind: 'Feature', statement: '', source: '', verifiedAt: today(), reviewBy: '' });
  proofDialog.value = true;
}

async function saveProof() {
  saving.value = true;
  try {
    await api.post(`${base.value}/proof`, {
      kind: proofForm.kind,
      statement: proofForm.statement.trim(),
      source: proofForm.source.trim(),
      verifiedAt: proofForm.verifiedAt,
      reviewBy: proofForm.reviewBy || null,
    });
    proofDialog.value = false;
    await load();
  } catch (e) {
    notifyError(e);
  } finally {
    saving.value = false;
  }
}

function retire(item: ProofItem) {
  $q.dialog({
    title: t('pages.brandProfile.proof.retire'),
    message: t('pages.brandProfile.proof.retireConfirm'),
    cancel: true,
    persistent: true,
  }).onOk(() => void doRetire(item));
}

async function doRetire(item: ProofItem) {
  try {
    await api.post(`${base.value}/proof/${item.id}/retire`);
    await load();
  } catch (e) {
    notifyError(e);
  }
}

const phraseDialog = ref(false);
const phraseForm = reactive({ text: '', source: '', collectedAt: today() });

function openPhrase() {
  Object.assign(phraseForm, { text: '', source: '', collectedAt: today() });
  phraseDialog.value = true;
}

async function savePhrase() {
  saving.value = true;
  try {
    await api.post(`${base.value}/phrases`, {
      text: phraseForm.text.trim(),
      source: phraseForm.source.trim(),
      collectedAt: phraseForm.collectedAt,
    });
    phraseDialog.value = false;
    await load();
  } catch (e) {
    notifyError(e);
  } finally {
    saving.value = false;
  }
}

const checkText = ref('');
const checking = ref(false);
const check = ref<CopyCheckResult | null>(null);
const findings = computed(() => sortFindings(check.value?.findings ?? []));

async function runCheck() {
  checking.value = true;
  try {
    const { data } = await api.post<CopyCheckResult>(`${base.value}/copy/check`, { text: checkText.value });
    check.value = data;
  } catch (e) {
    notifyError(e);
  } finally {
    checking.value = false;
  }
}

onMounted(load);
watch(() => brand.selected, () => {
  check.value = null;
  void load();
});
</script>

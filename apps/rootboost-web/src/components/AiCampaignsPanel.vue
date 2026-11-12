<template>
  <!-- Campanha pela IA (RB-31): a IA propõe a partir de um vídeo pronto, a pessoa confere e aprova, nasce PAUSADA na Meta. -->
  <div class="q-mt-xl">
    <!-- Em tela estreita o título ocupa a linha toda e o seletor desce (12/11: quebrava uma palavra por linha). -->
    <div class="row items-center q-col-gutter-sm q-mb-sm">
      <div class="col-12 col-md">
        <div class="text-h6 sora-bold">{{ $t('aiCampaigns.title') }}</div>
        <div class="text-grey-7">{{ $t('aiCampaigns.subtitle') }}</div>
      </div>
      <div class="col-12 col-sm col-md-5">
        <q-select v-model="jobId" outlined dense emit-value map-options :options="readyJobs"
          :label="$t('aiCampaigns.fromVideo')" :disable="!readyJobs.length" />
      </div>
      <div class="col-12 col-sm-auto">
        <q-btn unelevated color="primary" no-caps class="full-width" :label="$t('aiCampaigns.propose')" :loading="proposing"
          :disable="!jobId" @click="propose()" />
      </div>
    </div>
    <div v-if="!readyJobs.length" class="text-caption text-grey-7 q-mb-md">{{ $t('aiCampaigns.noVideo') }}</div>

    <!-- Ranking orgânico (RB-25): os vídeos que o público mais salvou, compartilhou e assistiu, com o motivo. -->
    <q-card flat bordered class="card-rounded q-pa-md q-mb-md">
      <div class="row items-center q-col-gutter-sm q-mb-sm">
        <div class="col-12 col-sm">
          <div class="text-subtitle1 text-weight-bold">{{ $t('aiCampaigns.ranking.title') }}</div>
          <div class="text-caption text-grey-7">{{ $t('aiCampaigns.ranking.subtitle') }}</div>
        </div>
        <div class="col-12 col-sm-auto">
          <q-btn outline color="primary" no-caps class="full-width" :icon="mdiRefresh" :label="$t('aiCampaigns.ranking.refresh')"
            :loading="syncing" @click="syncRanking" />
        </div>
      </div>
      <div v-if="!ranking.length" class="text-caption text-grey-7">{{ $t('aiCampaigns.ranking.empty') }}</div>
      <div v-for="r in ranking.slice(0, showAll ? 30 : 5)" :key="r.externalId" class="row items-start no-wrap q-py-sm rank-row">
        <div class="rank-pos text-h6 sora-bold q-mr-md" :class="r.enoughData ? 'text-primary' : 'text-grey-6'">{{ r.position }}</div>
        <div class="col" style="min-width: 0">
          <div class="row items-center q-gutter-xs">
            <q-chip dense :color="r.enoughData ? 'primary' : 'grey-6'" text-color="white">
              {{ r.enoughData ? $t('aiCampaigns.ranking.score', { score: r.score }) : $t('aiCampaigns.ranking.fewData') }}
            </q-chip>
            <span class="text-caption text-grey-7">{{ r.channel }} · {{ new Date(r.postedAt).toLocaleDateString() }}</span>
          </div>
          <div class="text-body2 ellipsis-2-lines">{{ r.caption.split('\n')[0] || '—' }}</div>
          <div class="text-caption text-grey-7">{{ r.reason }}</div>
          <div class="row items-center q-gutter-sm q-mt-xs">
            <a v-if="r.permalink" :href="r.permalink" target="_blank" rel="noopener" class="text-caption text-primary">{{ $t('aiCampaigns.ranking.open') }}</a>
            <q-btn v-if="canAdvertise(r)" dense flat no-caps color="primary" :label="$t('aiCampaigns.ranking.advertise')"
              :loading="proposing && jobId === `pub:${r.publicationId}`" @click="propose(`pub:${r.publicationId}`)" />
            <span v-else-if="r.enoughData" class="text-caption text-grey-6">{{ $t('aiCampaigns.ranking.notHere') }}</span>
          </div>
        </div>
      </div>
      <q-btn v-if="ranking.length > 5" flat dense no-caps color="primary" class="q-mt-xs"
        :label="showAll ? $t('aiCampaigns.ranking.less') : $t('aiCampaigns.ranking.more', { n: ranking.length })" @click="showAll = !showAll" />
    </q-card>

    <q-card v-for="c in campaigns" :key="c.id" flat bordered class="card-rounded q-pa-md q-mb-md">
      <div class="row items-center q-mb-sm">
        <div class="col text-subtitle1 text-weight-bold">{{ c.name }}</div>
        <q-chip dense :color="chipColor(c.status)" text-color="white">{{ $t(`aiCampaigns.status.${c.status}`) }}</q-chip>
      </div>
      <div v-if="c.rationale" class="text-caption text-grey-7 q-mb-sm">{{ c.rationale }}</div>
      <q-banner v-if="c.error" dense rounded class="bg-red-1 text-negative q-mb-sm">{{ c.error }}</q-banner>

      <template v-if="editable(c)">
        <div class="row q-col-gutter-sm">
          <q-input v-model="forms[c.id]!.name" outlined dense class="col-12 col-md-6" :label="$t('aiCampaigns.f.name')" />
          <q-select v-model="forms[c.id]!.objective" outlined dense emit-value map-options class="col-6 col-md-3" :options="objectives" :label="$t('aiCampaigns.f.objective')" />
          <q-input v-model.number="forms[c.id]!.dailyBudget" outlined dense type="number" step="1" min="6" max="500" class="col-6 col-md-3" :label="$t('aiCampaigns.f.budget')" prefix="R$" />
          <q-input v-model="countries[c.id]" outlined dense class="col-6 col-md-3" :label="$t('aiCampaigns.f.countries')" />
          <q-input v-model.number="forms[c.id]!.ageMin" outlined dense type="number" class="col-3 col-md-2" :label="$t('aiCampaigns.f.ageMin')" />
          <q-input v-model.number="forms[c.id]!.ageMax" outlined dense type="number" class="col-3 col-md-2" :label="$t('aiCampaigns.f.ageMax')" />
          <q-select v-model="forms[c.id]!.callToAction" outlined dense class="col-6 col-md-5" :options="ctas" :label="$t('aiCampaigns.f.cta')" />
          <q-input v-model="forms[c.id]!.primaryText" outlined dense autogrow type="textarea" class="col-12" :label="$t('aiCampaigns.f.text')" />
          <q-input v-model="forms[c.id]!.headline" outlined dense class="col-12 col-md-6" maxlength="255" :label="$t('aiCampaigns.f.headline')" />
          <q-input v-model="forms[c.id]!.linkUrl" outlined dense class="col-12 col-md-6" :label="$t('aiCampaigns.f.link')" />
        </div>
        <div class="row items-center q-gutter-sm q-mt-sm">
          <q-btn outline color="primary" no-caps :label="$t('aiCampaigns.save')" :loading="busy === c.id + 'save'" @click="save(c)" />
          <q-btn unelevated color="primary" no-caps :label="$t('aiCampaigns.approve')" :loading="busy === c.id + 'ok'" @click="approve(c)" />
          <span class="text-caption text-grey-7">{{ $t('aiCampaigns.pausedNote') }}</span>
        </div>
      </template>
      <div v-else class="text-body2">
        {{ c.primaryText }}
        <div class="text-caption text-grey-7 q-mt-xs">
          R$ {{ c.dailyBudget.toFixed(2) }}/{{ $t('aiCampaigns.day') }} · {{ c.countries.join(', ') }} · {{ c.ageMin }} a {{ c.ageMax }} anos
          <template v-if="c.externalCampaignId"> · {{ $t('aiCampaigns.metaId') }} {{ c.externalCampaignId }}</template>
        </div>
        <div v-if="c.status === 'Created' && !c.advice" class="text-caption text-positive q-mt-xs">{{ $t('aiCampaigns.createdNote') }}</div>
        <!-- Acompanhamento (RB-52): o que fazer agora, com os números que sustentam. Nada é aplicado sozinho. -->
        <q-banner v-if="c.advice" dense rounded class="q-mt-sm" :class="adviceClass(c.advice.kind)">
          <div class="text-weight-medium">{{ $t(`aiCampaigns.advice.${c.advice.kind}`) }}</div>
          <div class="text-caption">{{ c.advice.message }}</div>
          <div class="text-caption text-grey-8">
            R$ {{ c.advice.spend.toFixed(2) }} · {{ c.advice.impressions }} {{ $t('aiCampaigns.impressions') }} · {{ c.advice.clicks }} {{ $t('aiCampaigns.clicks') }}
            <template v-if="c.objective === 'leads'"> · {{ c.advice.conversions }} {{ $t('aiCampaigns.results') }}</template>
          </div>
          <template v-if="c.status === 'Created' && !c.pausedAt" #action>
            <q-btn flat no-caps color="negative" :label="$t('aiCampaigns.pause')" :loading="busy === c.id + 'pause'" @click="pause(c)" />
          </template>
        </q-banner>
      </div>
    </q-card>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, reactive, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import { useQuasar } from 'quasar';
import { isAxiosError } from 'axios';
import { api } from 'src/services/api';
import type { CreativeJob, Publication } from 'src/types/api';
import { mdiRefresh } from '@quasar/extras/mdi-v7';

defineOptions({ name: 'AiCampaignsPanel' });

interface AdCampaign {
  id: string; jobId: string | null; name: string; objective: 'trafego' | 'leads'; dailyBudget: number; countries: string[];
  ageMin: number; ageMax: number; videoUrl: string; thumbnailUrl: string | null; primaryText: string; headline: string;
  callToAction: string; linkUrl: string; rationale: string; status: 'Draft' | 'Approved' | 'Creating' | 'Created' | 'Failed';
  externalCampaignId: string | null; externalAdId: string | null; error: string | null; pausedAt: string | null;
  advice: { kind: 'Wait' | 'Keep' | 'Scale' | 'ReviewCreative' | 'Pause'; message: string; suggestedDailyBudget: number | null;
    spend: number; impressions: number; clicks: number; conversions: number; ctr: number | null } | null;
}
interface RankedPost {
  position: number; score: number; enoughData: boolean; engagementPercent: number; reason: string; channel: string;
  externalId: string; productType: string; caption: string; permalink: string | null; thumbnailUrl: string | null;
  postedAt: string; publicationId: string | null; views: number; reach: number; saves: number; shares: number;
}
type Form = Pick<AdCampaign, 'name' | 'objective' | 'dailyBudget' | 'ageMin' | 'ageMax' | 'primaryText' | 'headline' | 'callToAction' | 'linkUrl'>;

const props = defineProps<{ brandKey: string }>();
const { t } = useI18n();
const $q = useQuasar();

const campaigns = ref<AdCampaign[]>([]);
const jobs = ref<CreativeJob[]>([]);
const published = ref<Publication[]>([]);
const jobId = ref<string | null>(null);
const ranking = ref<RankedPost[]>([]);
const syncing = ref(false);
const showAll = ref(false);
const proposing = ref(false);
const busy = ref<string | null>(null);
const forms = reactive<Record<string, Form>>({});
const countries = reactive<Record<string, string>>({});

const objectives = computed(() => [
  { label: t('aiCampaigns.objective.trafego'), value: 'trafego' },
  { label: t('aiCampaigns.objective.leads'), value: 'leads' },
]);
const ctas = ['LEARN_MORE', 'SIGN_UP', 'SHOP_NOW', 'CONTACT_US', 'BOOK_NOW', 'GET_OFFER', 'SUBSCRIBE'];
// Fonte do anúncio: vídeo pronto do Estúdio ou vídeo já publicado (importado ou não), 12/11.
const readyJobs = computed(() => [
  ...jobs.value
    .filter((j) => (j.status === 'Review' || j.status === 'Done') && j.outputs.some((o) => o.role.startsWith('video')))
    .map((j) => ({ label: `${t('aiCampaigns.fromStudio')}: ${j.theme}`, value: `job:${j.id}` })),
  ...published.value
    .filter((p) => p.status === 'Published' && /\.(mp4|mov)(\?|$)/i.test(p.mediaUrl))
    .map((p) => ({ label: `${t('aiCampaigns.fromPost')} (${p.channel}): ${p.caption.split('\n')[0]?.slice(0, 70) ?? ''}`, value: `pub:${p.id}` })),
]);

const editable = (c: AdCampaign) => c.status === 'Draft' || c.status === 'Failed' || c.status === 'Approved';
const adviceClass = (k: NonNullable<AdCampaign['advice']>['kind']) =>
  ({ Wait: 'bg-blue-1 text-primary', Keep: 'bg-grey-2 text-grey-9', Scale: 'bg-green-1 text-positive', ReviewCreative: 'bg-amber-1 text-brown-9', Pause: 'bg-red-1 text-negative' })[k];
const chipColor = (s: AdCampaign['status']) => ({ Draft: 'grey-7', Approved: 'primary', Creating: 'primary', Created: 'positive', Failed: 'negative' })[s];

function fail(e: unknown) {
  const msg = isAxiosError(e) && typeof e.response?.data?.error === 'string' ? e.response.data.error : t('common.saveError');
  $q.notify({ type: 'negative', message: msg });
}

function fill(c: AdCampaign) {
  forms[c.id] = { name: c.name, objective: c.objective, dailyBudget: c.dailyBudget, ageMin: c.ageMin, ageMax: c.ageMax,
    primaryText: c.primaryText, headline: c.headline, callToAction: c.callToAction, linkUrl: c.linkUrl };
  countries[c.id] = c.countries.join(', ');
}

async function load() {
  const from = new Date(Date.now() - 180 * 864e5).toISOString();
  const [cs, js, ps] = await Promise.all([
    api.get<AdCampaign[]>(`/admin/brands/${props.brandKey}/ads/campaigns`),
    api.get<CreativeJob[]>(`/admin/brands/${props.brandKey}/creative-jobs`),
    api.get<Publication[]>(`/admin/brands/${props.brandKey}/publications`, { params: { from, to: new Date().toISOString() } }),
  ]);
  campaigns.value = cs.data;
  jobs.value = js.data;
  published.value = ps.data;
  campaigns.value.forEach(fill);
  await loadRanking();
}

async function loadRanking() {
  try {
    ranking.value = (await api.get<RankedPost[]>(`/admin/brands/${props.brandKey}/organic/ranking`, { params: { days: 180 } })).data;
  } catch { ranking.value = []; }
}

// Só dá pra anunciar o que saiu pela plataforma: é o arquivo do vídeo que vai pra Meta.
const canAdvertise = (r: RankedPost) => !!r.publicationId && readyJobs.value.some((o) => o.value === `pub:${r.publicationId}`);

async function syncRanking() {
  syncing.value = true;
  try {
    await api.post(`/admin/brands/${props.brandKey}/organic/sync`, null, { timeout: 120_000 });
    await loadRanking();
  } catch (e) { fail(e); } finally { syncing.value = false; }
}

function upsert(c: AdCampaign) {
  const i = campaigns.value.findIndex((x) => x.id === c.id);
  if (i >= 0) campaigns.value[i] = c; else campaigns.value.unshift(c);
  fill(c);
}

async function propose(source?: string) {
  if (source) jobId.value = source;
  proposing.value = true;
  try {
    const [kind, id] = (jobId.value ?? '').split(':');
    const body = kind === 'pub' ? { publicationId: id } : { jobId: id };
    upsert((await api.post<AdCampaign>(`/admin/brands/${props.brandKey}/ads/campaigns`, body, { timeout: 120_000 })).data);
  } catch (e) { fail(e); } finally { proposing.value = false; }
}

async function save(c: AdCampaign) {
  busy.value = c.id + 'save';
  try {
    const body = { ...forms[c.id], countries: countries[c.id]!.split(',').map((s) => s.trim()).filter(Boolean) };
    upsert((await api.put<AdCampaign>(`/admin/brands/${props.brandKey}/ads/campaigns/${c.id}`, body)).data);
    return true;
  } catch (e) { fail(e); return false; } finally { busy.value = null; }
}

function approve(c: AdCampaign) {
  // Aprovação explícita: cria na Meta, mas pausada. Gasto só começa quando a pessoa ativa no Gerenciador de Anúncios.
  $q.dialog({ title: t('aiCampaigns.confirmTitle'), message: t('aiCampaigns.confirmText', { budget: forms[c.id]!.dailyBudget }), cancel: true })
    .onOk(() => void createPaused(c));
}

async function createPaused(c: AdCampaign) {
      if (!(await save(c))) return;
      busy.value = c.id + 'ok';
      try {
        upsert((await api.post<AdCampaign>(`/admin/brands/${props.brandKey}/ads/campaigns/${c.id}/approve`, {}, { timeout: 300_000 })).data);
        $q.notify({ type: 'positive', message: t('aiCampaigns.createdNote') });
      } catch (e) {
        if (isAxiosError(e) && e.response?.data?.campaign) upsert(e.response.data.campaign as AdCampaign);
        fail(e);
      } finally { busy.value = null; }
}

function pause(c: AdCampaign) {
  $q.dialog({ title: t('aiCampaigns.pauseTitle'), message: t('aiCampaigns.pauseText'), cancel: true }).onOk(() => void doPause(c));
}

async function doPause(c: AdCampaign) {
  busy.value = c.id + 'pause';
  try {
    upsert((await api.post<AdCampaign>(`/admin/brands/${props.brandKey}/ads/campaigns/${c.id}/pause`)).data);
  } catch (e) { fail(e); } finally { busy.value = null; }
}

watch(() => props.brandKey, () => void load().catch(fail));
onMounted(() => void load().catch(fail));
</script>

<style scoped>
.rank-pos { width: 28px; text-align: center; flex-shrink: 0; }
.rank-row + .rank-row { border-top: 1px solid rgba(127, 127, 127, 0.15); }
</style>

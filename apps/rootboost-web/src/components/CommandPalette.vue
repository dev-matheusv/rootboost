<template>
  <!-- Busca rápida (Ctrl+K ou Cmd+K): ir pra uma página, disparar uma ação ou abrir um post agendado. -->
  <q-dialog v-model="open" position="top" transition-show="jump-down" transition-hide="fade" @show="onShow">
    <q-card class="palette card-rounded">
      <q-input ref="inputEl" v-model="query" borderless autofocus class="q-px-md" :placeholder="$t('palette.placeholder')"
        @keydown.down.prevent="move(1)" @keydown.up.prevent="move(-1)" @keydown.enter.prevent="runActive">
        <template #prepend><q-icon :name="mdiMagnify" /></template>
        <template #append><span class="kbd">Esc</span></template>
      </q-input>
      <q-separator />
      <q-list class="results q-py-xs">
        <template v-for="(c, i) in results" :key="c.id">
          <q-item-label v-if="i === 0 || results[i - 1]!.group !== c.group" header class="q-py-xs">{{ c.group }}</q-item-label>
          <q-item clickable dense :active="i === active" active-class="palette-active" @click="run(c)" @mousemove="active = i">
            <q-item-section>{{ c.label }}</q-item-section>
            <q-item-section v-if="c.hint" side class="text-caption">{{ c.hint }}</q-item-section>
          </q-item>
        </template>
        <q-item v-if="!results.length"><q-item-section class="text-grey-6">{{ $t('palette.empty') }}</q-item-section></q-item>
      </q-list>
    </q-card>
  </q-dialog>
</template>

<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import { useQuasar } from 'quasar';
import { useRouter } from 'vue-router';
import { mdiMagnify } from '@quasar/extras/mdi-v7';
import { api } from 'src/services/api';
import type { Publication } from 'src/types/api';
import { useBrandStore } from 'src/stores/brandStore';
import { filterCommands, type Command } from 'src/utils/commands';
import { addDays, bundlePosts } from 'src/utils/publications';

defineOptions({ name: 'CommandPalette' });

const open = defineModel<boolean>({ default: false });
const { t, locale } = useI18n();
const $q = useQuasar();
const router = useRouter();
const brand = useBrandStore();

const query = ref('');
const active = ref(0);
const posts = ref<Command[]>([]);

const go = (name: string, query: Record<string, string> = {}) => () => void router.push({ name, query });

const base = computed<Command[]>(() => {
  const nav = t('palette.groups.nav');
  const act = t('palette.groups.actions');
  return [
    { id: 'publications', group: nav, label: t('nav.publications'), keywords: 'agenda calendario fila posts', run: go('publications') },
    { id: 'overview', group: nav, label: t('nav.overview'), keywords: 'inicio painel', run: go('overview') },
    { id: 'campaigns', group: nav, label: t('nav.campaigns'), keywords: 'anuncios ads meta google trafego', run: go('campaigns') },
    { id: 'brand', group: nav, label: t('nav.brandProfile'), keywords: 'voz prova marca', run: go('brandProfile') },
    { id: 'orders', group: nav, label: t('nav.orders'), keywords: 'vendas loja', run: go('orders') },
    { id: 'new', group: act, label: t('pages.publications.new'), keywords: 'criar publicar', run: go('publications', { action: 'new' }) },
    { id: 'import', group: act, label: t('reelImport.title'), keywords: 'reel video subir', run: go('publications', { action: 'import' }) },
    { id: 'dark', group: act, label: t('nav.darkMode'), keywords: 'tema claro escuro', run: () => $q.dark.toggle() },
  ];
});

const results = computed(() => filterCommands([...base.value, ...posts.value], query.value));
watch(results, () => (active.value = 0));

/** Posts da marca escolhida (três semanas pra trás, quatro meses pra frente), um por grupo de canais. */
async function loadPosts() {
  posts.value = [];
  if (!brand.selected) return;
  try {
    const now = new Date();
    const { data } = await api.get<Publication[]>(`/admin/brands/${brand.selected}/publications`, {
      params: { from: addDays(now, -21).toISOString(), to: addDays(now, 120).toISOString() },
    });
    const group = t('palette.groups.posts');
    posts.value = bundlePosts(data).map((b) => ({
      id: `post:${b.key}`,
      group,
      label: b.title,
      hint: new Date(b.scheduledFor).toLocaleDateString(locale.value, { day: '2-digit', month: '2-digit' }),
      keywords: b.items.map((p) => p.channel).join(' '),
      run: go('publications', { post: b.key }),
    }));
  } catch {
    // Sem posts na busca: páginas e ações continuam funcionando.
  }
}

function onShow() {
  query.value = '';
  active.value = 0;
  void loadPosts();
}

function move(n: number) {
  const len = results.value.length;
  if (len) active.value = (active.value + n + len) % len;
}

function run(c: Command) {
  open.value = false;
  c.run();
}

function runActive() {
  const c = results.value[active.value];
  if (c) run(c);
}

function onKey(e: KeyboardEvent) {
  if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'k') {
    e.preventDefault();
    open.value = !open.value;
  }
}

onMounted(() => window.addEventListener('keydown', onKey));
onBeforeUnmount(() => window.removeEventListener('keydown', onKey));
</script>

<style lang="scss" scoped>
.palette { width: 600px; max-width: 94vw; margin-top: 10vh; }
.results { max-height: 55vh; overflow-y: auto; }
.palette-active { background: $light-focus; color: $primary; }
.body--dark .palette-active { background: $dark-focus; color: #a98bff; }
.kbd { font-size: 11px; padding: 2px 6px; border-radius: 6px; border: 1px solid rgba(128, 128, 128, 0.35); }
</style>

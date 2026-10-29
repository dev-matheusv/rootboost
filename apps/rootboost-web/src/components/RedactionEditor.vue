<template>
  <q-dialog :model-value="modelValue" @update:model-value="emit('update:modelValue', $event)" @show="open">
    <q-card style="width: 720px; max-width: 96vw" class="q-pa-sm">
      <q-card-section class="text-h6">{{ $t('privacy.editorTitle') }}</q-card-section>
      <q-card-section class="column q-gutter-y-sm">
        <div class="text-caption text-grey-7">{{ $t('privacy.editorHint') }}</div>
        <q-btn-toggle v-model="status" spread no-caps unelevated rounded toggle-color="primary" size="sm"
          :options="(['None', 'Fictional', 'Uncertain', 'Real'] as const).map((s) => ({ value: s, label: $t(`privacy.status.${s}`) }))" />
        <div class="stage">
          <!-- O quadro tem o tamanho exato da foto: as áreas ficam em fração dela. -->
          <div v-if="src" class="frame" @pointerdown="down" @pointermove="move" @pointerup="up" @pointerleave="up">
            <img ref="img" :src="src" alt="" draggable="false" />
            <div v-for="(b, i) in boxes" :key="i" class="box" :style="style(b)">
              <q-btn round dense size="xs" color="negative" :icon="mdiClose" class="del" @pointerdown.stop @click.stop="boxes.splice(i, 1)" />
            </div>
            <div v-if="draft" class="box draft" :style="style(draft)" />
          </div>
          <div v-else class="q-pa-xl text-center text-grey-7"><q-spinner size="28px" /></div>
        </div>
      </q-card-section>
      <q-card-actions align="right">
        <q-btn flat no-caps :label="$t('common.actions.cancel')" v-close-popup />
        <q-btn unelevated color="primary" no-caps :label="$t('privacy.save')" :loading="busy" @click="save" />
      </q-card-actions>
    </q-card>
  </q-dialog>
</template>

<script setup lang="ts">
import { onBeforeUnmount, ref } from 'vue';
import { useQuasar } from 'quasar';
import { useI18n } from 'vue-i18n';
import { isAxiosError } from 'axios';
import { mdiClose } from '@quasar/extras/mdi-v7';
import { api } from 'src/services/api';

interface Box { x: number; y: number; w: number; h: number }
type Status = 'None' | 'Fictional' | 'Real' | 'Uncertain';
export interface RedactableMedia { id: string; personalData: string; redactionBoxes: Box[] }

const props = defineProps<{ modelValue: boolean; media: RedactableMedia | null; base: string }>();
const emit = defineEmits<{ 'update:modelValue': [boolean]; saved: [unknown] }>();
const { t } = useI18n();
const $q = useQuasar();
const src = ref<string | null>(null);
const img = ref<HTMLImageElement | null>(null);
const boxes = ref<Box[]>([]);
const status = ref<Status>('Uncertain');
const draft = ref<Box | null>(null);
const busy = ref(false);
let start: { x: number; y: number } | null = null;

/** Mostra o ORIGINAL (com login) pra pessoa ver o que está debaixo do borrado e marcar certo. */
async function open() {
  if (!props.media) return;
  boxes.value = props.media.redactionBoxes.map((b) => ({ ...b }));
  status.value = (props.media.personalData === 'Unchecked' ? 'Uncertain' : props.media.personalData) as Status;
  if (src.value) URL.revokeObjectURL(src.value);
  src.value = null;
  const { data } = await api.get<Blob>(`${props.base}/${props.media.id}/original`, { responseType: 'blob' });
  src.value = URL.createObjectURL(data);
}

function point(e: PointerEvent) {
  const r = img.value?.getBoundingClientRect();
  if (!r) return null;
  return { x: Math.min(1, Math.max(0, (e.clientX - r.left) / r.width)), y: Math.min(1, Math.max(0, (e.clientY - r.top) / r.height)) };
}

function down(e: PointerEvent) {
  start = point(e);
  if (start) (e.currentTarget as HTMLElement).setPointerCapture(e.pointerId);
}

function move(e: PointerEvent) {
  const p = start && point(e);
  if (!start || !p) return;
  draft.value = { x: Math.min(start.x, p.x), y: Math.min(start.y, p.y), w: Math.abs(p.x - start.x), h: Math.abs(p.y - start.y) };
}

function up() {
  if (draft.value && draft.value.w > 0.01 && draft.value.h > 0.01) boxes.value.push(draft.value);
  draft.value = null;
  start = null;
}

function style(b: Box) {
  return { left: `${b.x * 100}%`, top: `${b.y * 100}%`, width: `${b.w * 100}%`, height: `${b.h * 100}%` };
}

async function save() {
  busy.value = true;
  try {
    const { data } = await api.put(`${props.base}/${props.media!.id}/redaction`, { status: status.value, boxes: boxes.value });
    emit('saved', data);
    emit('update:modelValue', false);
  } catch (e) {
    $q.notify({ type: 'negative', message: isAxiosError(e) && typeof e.response?.data?.error === 'string' ? e.response.data.error : t('common.saveError') });
  } finally {
    busy.value = false;
  }
}

onBeforeUnmount(() => { if (src.value) URL.revokeObjectURL(src.value); });
</script>

<style lang="scss" scoped>
.stage { display: flex; justify-content: center; background: #05040b; border-radius: 8px; overflow: hidden; }
.frame { position: relative; user-select: none; touch-action: none; cursor: crosshair; line-height: 0; }
.frame img { display: block; max-width: 100%; max-height: 70vh; pointer-events: none; }
.box { position: absolute; border: 2px solid #ff7a59; background: rgba(255, 122, 89, 0.35); backdrop-filter: blur(6px); }
.box.draft { border-style: dashed; }
.del { position: absolute; top: -10px; right: -10px; }
</style>

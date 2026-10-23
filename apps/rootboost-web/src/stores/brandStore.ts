import { defineStore } from 'pinia';
import { api } from 'src/services/api';
import type { Brand } from 'src/types/api';

const KEY = 'rootboost.brand';

/** Parâmetros de consulta pra marca escolhida. Vazio = todas as marcas. */
export function brandParams(brand: string): Record<string, string> {
  return brand ? { brand } : {};
}

function readSaved(): string {
  try {
    return localStorage.getItem(KEY) ?? '';
  } catch {
    return '';
  }
}

export const useBrandStore = defineStore('brand', {
  state: () => ({
    brands: [] as Brand[],
    selected: readSaved(),
    loaded: false,
  }),

  actions: {
    async load() {
      const { data } = await api.get<Brand[]>('/admin/brands');
      this.brands = data;
      this.loaded = true;
      // Marca salva que deixou de existir volta pra "todas".
      if (this.selected && !data.some((b) => b.key === this.selected)) this.select('');
    },

    select(key: string) {
      this.selected = key;
      try {
        localStorage.setItem(KEY, key);
      } catch {
        // Sem storage (aba privada): a escolha vale só nesta sessão.
      }
    },
  },
});

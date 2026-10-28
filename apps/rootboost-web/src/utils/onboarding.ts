/** Cadastro do produto (RB-60), no formato da API. */
export interface ProductForm {
  name: string;
  kind: string;
  url: string;
  appUrl: string;
  summary: string;
  audience: string;
  features: string[];
  differentials: string[];
  offer: string;
}

/** Proposta que a IA devolve a cada resposta (campos vazios = ainda não sabe). */
export interface ProductProposal {
  name: string | null;
  kind: string | null;
  url: string | null;
  appUrl: string | null;
  summary: string | null;
  audience: string | null;
  features: string[];
  differentials: string[];
  offer: string | null;
}

export const emptyProduct = (): ProductForm => ({
  name: '', kind: 'app', url: '', appUrl: '', summary: '', audience: '', features: [], differentials: [], offer: '',
});

/**
 * Junta a proposta da IA no formulário: o que ela sabe preenche, o que veio vazio não apaga o que já estava
 * (nem o que a pessoa digitou).
 */
export function mergeProposal(form: ProductForm, p: ProductProposal): ProductForm {
  const pick = (v: string | null | undefined, old: string) => (v && v.trim() ? v.trim() : old);
  return {
    name: pick(p.name, form.name),
    kind: pick(p.kind, form.kind),
    url: pick(p.url, form.url),
    appUrl: pick(p.appUrl, form.appUrl),
    summary: pick(p.summary, form.summary),
    audience: pick(p.audience, form.audience),
    features: p.features.length ? p.features : form.features,
    differentials: p.differentials.length ? p.differentials : form.differentials,
    offer: pick(p.offer, form.offer),
  };
}

/** Já dá pra salvar: o mínimo pra IA escrever roteiro bom (o que é, pra quem e o que faz). */
export function readyToSave(f: ProductForm): boolean {
  return !!f.name.trim() && !!f.summary.trim() && !!f.audience.trim() && f.features.length > 0;
}

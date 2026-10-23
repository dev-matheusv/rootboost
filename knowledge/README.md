# knowledge/ — o conhecimento de marketing da plataforma

Vale para **toda** marca que o RootBoost atende. Nada aqui é de um cliente: exemplos aparecem só como
ilustração, marcados com `[exemplo: marca]`. O que é de uma marca (voz, oferta, prova, palavras dos clientes
dela) fica no **perfil da marca**, no banco, nunca aqui. O MedlyCare é a primeira marca (cliente zero).

Origem: `Documents\RootBoost Conhecimento\` (versão 1, 23/10/2026), que veio da prática no cliente zero e da
literatura clássica de resposta direta. Desenho do produto em `docs/BASE-DE-CONHECIMENTO.md`.

## Arquivos

| Arquivo | O que tem | Quem usa |
|---|---|---|
| `copy/playbook.md` | ciclo de copy, níveis de consciência, fórmulas, ganchos, estilo, modelos de decisão, objeções | prompt da IA |
| `copy/levers.json` | gatilhos e vieses: quando é legítimo, quando nunca usar, prova exigida, evidência, regra legal | IA e checklist |
| `copy/hooks.json` | tipos de gancho, níveis de consciência e fórmulas como etiquetas | etiquetas dos criativos, teste |
| `copy/checklist.json` | conferência antes de publicar: o que bloqueia, o que dá nota, o que é automático | checklist automática |
| `channels/limits.json` | limites de caractere por canal, com a data em que foram conferidos | checklist, geração |
| `channels/policies.json` | o que Meta e Google recusam, e a regra do CDC | checklist, geração |
| `video/formats.json` | formatos, zonas seguras, estrutura por tipo de vídeo, voz e legenda | DMaker (specs), checklist |
| `ads/testing.json` | matriz de teste, regras de pausa e escala, métricas em ordem, nome e UTM | advisor, Console |
| `learnings/learnings.json` | o que os testes ensinaram, por nicho (só etiqueta e número agregado) | prior do teste, Console |

Os JSONs são validados por teste (`KnowledgeBaseTests`): arquivo quebrado não passa no CI.

## Regra de evolução

1. Aprendeu algo num cliente? Se vale pra outros, escreve aqui **de forma geral**.
2. Toda regra nova tem data, de onde veio (teste, pesquisa, estudo) e nível de confiança.
3. **Teste que contradiz uma regra ganha da regra:** atualizar e registrar o porquê.
4. **Nunca** copiar texto ou vídeo de um cliente pra cá. Só o princípio e o número agregado.
5. Mudar conhecimento é **PR com o porquê**, no mesmo fluxo do código.

## Como a IA usa (regra de ouro)

O inventário de prova da marca diz **o que é verdade**; este conhecimento diz **como dizer**. A IA entende a
empresa, a oferta e o público e escreve copy nova, humana e persuasiva, que só afirma o que é verdade. Nunca
copia trecho de documento pro anúncio.

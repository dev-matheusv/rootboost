# Pacote de Criativos — TidyRide (organizador de banco de carro)

Direção criativa pronta pra Meta Ads. Mercado principal: **EUA** (armazém US, entrega ~1 semana).
Copy voltada ao cliente segue o padrão: sem hífen e sem travessão.

> Mídia que já temos (reais, da CJ): `landing/car/media/` (hero.jpg, g1..g7). Vídeo demo 38MB (`hero.mp4`, comprimir).
> Geração por IA (Higgsfield) está bloqueada por bug de upload da CLI + créditos; usar as fotos reais por ora.

---

## Público (segmentação Meta)
- **Pais/famílias** (crianças pequenas, viagens de carro) — núcleo.
- **Motoristas de app** (Uber/99/Lyft) — mantêm o carro apresentável.
- **Donos de carro novo / entusiastas de acessórios automotivos**.
- Interesses: road trips, car accessories, parenting, minivan/SUV, Uber driver.
- Começar **US, en**; depois testar ES (LatAm/US Hispanic).

---

## Hooks de vídeo (primeiros 3s) — EN
1. "Your back seat does not have to look like a trash can."
2. "POV: your car is finally organized and the kids stop asking for snacks every 2 minutes."
3. "The one thing every parent needs before a road trip."
4. "I stopped cleaning my car every week when I added this."
5. "Uber drivers are all buying this right now."

### Hooks — ES
1. "Tu asiento trasero no tiene que parecer un basurero."
2. "POV: tu coche por fin está ordenado y los niños dejan de pedir cosas."
3. "Lo que todo papá necesita antes de un viaje."

---

## Roteiro do vídeo demo (~30s, UGC, vertical 9:16) — EN
- **[0-3s] Hook** (câmera no banco de trás bagunçado: lixo, garrafas, brinquedos)
  "Your back seat does not have to look like this."
- **[3-8s] Solução** (prende o TidyRide no encosto em 1 minuto)
  "This hangs on any seat in about a minute, no tools."
- **[8-18s] Demo/benefício** (abre a bandeja, encaixa o tablet, enche os bolsos)
  "Fold down tray for snacks, a holder for the tablet so the kids are happy, and a pocket for everything else."
- **[18-25s] Prova/uso** (viagem, criança assistindo, tudo no lugar)
  "Road trips just got so much calmer."
- **[25-30s] CTA**
  "Tap the link and tidy your car today."

---

## Anúncios estáticos (usar as fotos reais)
1. **Hero produto** (`hero.jpg` / g1) — Headline: "Tidy car, happy kids." · Texto: "Fold down tray, tablet holder and pockets for everything. Fits any seat, no tools. 30 day guarantee."
2. **Lifestyle 2 bancos** (`g4.jpg`) — Headline: "Every seat, sorted." · Texto: "Snacks, bottles, chargers and toys each get a spot. Ships from the US in about a week."
3. **Bolsos/tablet** (`g6.png`) — Headline: "The road trip hero." · Texto: "Keep the kids entertained and the mess gone. Get yours today."

> Todas as fotos reais têm texto de marketing embutido (padrão CJ). Antes de escalar: recortar o texto
> ou gerar versões limpas (Higgsfield quando destravar, ou editor de imagem).

---

## Plano de teste de criativo (casa com o cérebro de otimização já pronto)
- Rodar 3-5 **hooks** de vídeo no mesmo criativo base (1 ad set, orçamento pequeno).
- Deixar rodar até volume mínimo (o `CampaignOptimizer` decide escalar/cortar por ROAS; `CreativeSelector` elege o hook vencedor por CPA/CTR).
- Conversão já é medida server-side (Meta CAPI) + Pixel, deduplicada por PaymentId.
- Só ligar tráfego pago **depois** do produto validar (orgânico ou teste pequeno) e do checkout LIVE.

---

## O que falta pra produzir os criativos de IA
- **Higgsfield:** bug de upload de imagem na CLR (erro de assinatura S3) + plano free com poucos créditos.
  Alternativas: usar o app web do Higgsfield, subir crédito, ou outro gerador. Enquanto isso, as fotos reais + este pacote bastam.
- **Vídeo:** comprimir `hero.mp4` (38MB) pra usar como hero/anúncio (precisa de ferramenta de vídeo).

// Formatos devolvidos pela RootBoost.Api. Espelham os records do backend; mudou lá, muda aqui.

/** `POST /auth/login` (LoginResponse). */
export interface LoginResponse {
  token: string;
  expiresAtUtc: string;
  name: string;
  email: string;
}

/** `GET /orders` (OrderDto). */
export interface Order {
  paymentId: string;
  productKey: string;
  quantity: number;
  status: string;
  email: string;
  supplierOrderId: string | null;
  tracking: string | null;
  amount: number;
  currency: string;
  shipName: string;
  shipCountry: string;
  createdAt: string;
  failureReason: string | null;
  brandKey: string;
  /** Pagamento de teste (sandbox, Stripe em modo teste): não conta em pedidos nem receita. */
  isTest: boolean;
}

/** `GET /admin/brands`. */
export interface Brand {
  key: string;
  name: string;
  defaultLanguage: string;
}

/** Item do `GET /admin/products/performance` (ProductPerformance). */
export interface ProductPerformance {
  productKey: string;
  displayName: string | null;
  views: number;
  orders: number;
  revenue: number;
  currency: string;
  /** Nulo = sem visita medida (não é 0%). */
  conversionRate: number | null;
  revenuePerVisitor: number | null;
  fulfillable: boolean;
  enoughData: boolean;
}

/** `GET /admin/products/performance` (PerformanceReport). */
export interface PerformanceReport {
  generatedAt: string;
  since: string | null;
  products: ProductPerformance[];
  note: string;
}

/** Objeção comum e a resposta da marca (BrandObjection). */
export interface BrandObjection {
  objection: string;
  answer: string;
}

/** Voz da marca (BrandVoice). `PUT /admin/brands/{key}/voice`. */
export interface BrandVoice {
  audience: string;
  tone: string[];
  wordSwaps: Record<string, string>;
  bannedTerms: string[];
  objections: BrandObjection[];
}

export type ProofKind = 'Offer' | 'Feature' | 'Price' | 'Metric' | 'Testimonial' | 'Credential' | 'Scarcity' | 'Forbidden';

/** Item do inventário de prova. A IA só afirma o que está ativo aqui. */
export interface ProofItem {
  id: string;
  kind: ProofKind;
  statement: string;
  source: string;
  verifiedAt: string;
  reviewBy: string | null;
  active: boolean;
  needsReview: boolean;
}

/** Frase real de cliente, com a fonte. */
export interface CustomerPhrase {
  id: string;
  text: string;
  source: string;
  collectedAt: string;
}

/** `GET /admin/brands/{key}/profile`. */
export interface BrandProfile {
  brand: Brand;
  voice: BrandVoice;
  proof: ProofItem[];
  phrases: CustomerPhrase[];
}

/** Achado da checklist de copy (`POST /admin/brands/{key}/copy/check`). */
export interface CopyFinding {
  id: string;
  group: string;
  status: 'Pass' | 'Fail' | 'NeedsHuman' | 'Suggestion';
  blocking: boolean;
  message: string;
  fix: string | null;
}

export interface CopyCheckResult {
  blocked: boolean;
  findings: CopyFinding[];
}

/** Métricas de anúncio somadas (AdMetrics). Taxas nulas quando não há denominador. */
export interface AdMetrics {
  spend: number;
  impressions: number;
  clicks: number;
  reach: number;
  conversions: number;
  ctr: number | null;
  cpc: number | null;
  cpm: number | null;
  cpa: number | null;
}

export interface AdsAlert {
  /** 'stopRule' ou 'mixedCurrency' hoje; outros tipos podem chegar. */
  kind: string;
  objectId: string;
  name: string;
  message: string;
}

/** `GET /admin/brands/{key}/ads/summary`. */
export interface AdsSummary {
  brandKey: string;
  since: string;
  until: string;
  currencies: string[];
  totals: AdMetrics;
  campaigns: { campaignId: string; name: string; currency: string; metrics: AdMetrics }[];
  daily: { date: string; metrics: AdMetrics }[];
  alerts: AdsAlert[];
}

/** `GET /admin/ads/accounts`. */
export interface AdsAccounts {
  reader: string;
  accounts: { brandKey: string; platform: string; accountId: string }[];
}

export type SocialChannel = 'Instagram' | 'Facebook' | 'LinkedIn';

/** `GET /admin/alerts`: avisos de operação abertos (RB-58). */
export interface OpsAlert {
  id: string;
  kind: 'PublicationFailed' | 'AdsSyncFailed' | 'MetaConnection';
  key: string;
  brandKey: string | null;
  message: string;
  count: number;
  firstSeenAt: string;
  lastSeenAt: string;
}
export type PublicationStatus = 'Draft' | 'Approved' | 'Publishing' | 'Published' | 'Failed';

/** `GET /admin/brands/{key}/publications` (PublicationDto). */
export interface Publication {
  id: string;
  brandKey: string;
  channel: SocialChannel;
  mediaUrl: string;
  caption: string;
  captionVersion: number;
  scheduledFor: string;
  status: PublicationStatus;
  approvedBy: string | null;
  approvedAt: string | null;
  approvedCaptionVersion: number | null;
  externalId: string | null;
  publishedUrl: string | null;
  error: string | null;
  attempts: number;
  /** Reels: capa própria (JPEG), quadro da capa em ms e se aparece também no feed. */
  coverUrl: string | null;
  thumbOffsetMs: number | null;
  shareToFeed: boolean;
}

/** Estúdio (RB-42/RB-51): job de criativo que o worker local renderiza. */
export type CreativeKind = 'Organic' | 'Ad' | 'Prospecting';
export type CreativeJobStatus = 'Draft' | 'ScriptReady' | 'Queued' | 'Rendering' | 'Review' | 'Done' | 'Failed';

export interface CreativeOutput {
  role: string;
  url: string;
}

export interface CreativeJob {
  id: string;
  brandKey: string;
  kind: CreativeKind;
  theme: string;
  voiceKey: string | null;
  status: CreativeJobStatus;
  roteiro: unknown;
  falas: unknown;
  seed: number | null;
  warnings: string[];
  outputs: CreativeOutput[];
  attempts: number;
  progress: number;
  progressNote: string | null;
  error: string | null;
  createdAt: string;
  updatedAt: string;
}

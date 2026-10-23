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
  conversionRate: number;
  revenuePerVisitor: number;
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

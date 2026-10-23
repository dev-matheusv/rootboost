// Cor do chip por status do pedido. Espelha o enum OrderStatus do Domain
// (Placed, PlacedAtSupplier, Shipped, Delivered, Failed).
const COLORS: Record<string, string> = {
  Placed: 'grey-6',
  PlacedAtSupplier: 'info',
  Shipped: 'primary',
  Delivered: 'positive',
  Failed: 'negative',
};

export function statusColor(status: string): string {
  return COLORS[status] ?? 'grey-6';
}

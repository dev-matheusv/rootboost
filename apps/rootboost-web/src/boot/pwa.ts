import { defineBoot } from '#q-app/wrappers';

// Registra o service worker só no build de produção: no `quasar dev` ele atrapalharia o recarregamento automático.
export default defineBoot(() => {
  if (!import.meta.env.PROD || !('serviceWorker' in navigator)) return;
  const register = () =>
    navigator.serviceWorker.register('/sw.js').catch(() => {
      // Sem service worker o Console funciona igual; só não fica instalável.
    });
  // O boot pode rodar depois do "load" (módulos carregam de forma assíncrona): aí registra na hora.
  if (document.readyState === 'complete') void register();
  else window.addEventListener('load', () => void register(), { once: true });
});

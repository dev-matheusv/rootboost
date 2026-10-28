// Service worker do Console (RB-30). Existe pra deixar o Console instalável e receber os avisos (push, RB-37).
// De propósito NÃO guarda cache de tela: o MedlyCare já sofreu com cache servindo versão velha depois de deploy.
// Tudo vem da rede, como numa aba comum.
self.addEventListener('install', () => self.skipWaiting());
self.addEventListener('activate', (event) => event.waitUntil(self.clients.claim()));
self.addEventListener('fetch', () => {
  // Sem respondWith: o navegador busca normalmente.
});

// Aviso do servidor: { title, body, url, tag }. Mesmo tag substitui o anterior em vez de empilhar.
self.addEventListener('push', (event) => {
  let data = {};
  try {
    data = event.data ? event.data.json() : {};
  } catch {
    data = { body: event.data ? event.data.text() : '' };
  }
  event.waitUntil(
    self.registration.showNotification(data.title || 'Console', {
      body: data.body || '',
      tag: data.tag || undefined,
      icon: '/icons/icon-192.png',
      badge: '/icons/icon-192.png',
      data: { url: data.url || '/' },
    }),
  );
});

// Tocar no aviso: volta pra aba do Console já aberta (na tela do aviso) ou abre uma nova.
self.addEventListener('notificationclick', (event) => {
  event.notification.close();
  const url = new URL(event.notification.data?.url || '/', self.location.origin).href;
  event.waitUntil(
    self.clients.matchAll({ type: 'window', includeUncontrolled: true }).then((tabs) => {
      const tab = tabs.find((t) => t.url.startsWith(self.location.origin));
      if (tab) return tab.navigate(url).then((t) => (t || tab).focus());
      return self.clients.openWindow(url);
    }),
  );
});

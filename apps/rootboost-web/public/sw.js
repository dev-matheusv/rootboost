// Service worker do Console (RB-30). Existe pra deixar o Console instalável e, no RB-37, receber push.
// De propósito NÃO guarda cache de tela: o MedlyCare já sofreu com cache servindo versão velha depois de deploy.
// Tudo vem da rede, como numa aba comum.
self.addEventListener('install', () => self.skipWaiting());
self.addEventListener('activate', (event) => event.waitUntil(self.clients.claim()));
self.addEventListener('fetch', () => {
  // Sem respondWith: o navegador busca normalmente.
});

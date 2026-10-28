// Configuração do Console. Molde do front do MedlyCare (C:/SFA_Sistema/apps/web), sem os
// módulos que o RootBoost não usa (Capacitor, calendário, editor rico).
// https://v2.quasar.dev/quasar-cli-vite/quasar-config-file

import { defineConfig } from '#q-app/wrappers';
import { fileURLToPath } from 'node:url';

export default defineConfig((ctx) => {
  return {
    boot: ['i18n', 'axios'],

    css: ['app.scss'],

    extras: ['mdi-v7'],

    build: {
      target: {
        browser: ['es2022', 'firefox115', 'chrome115', 'safari14'],
        node: 'node20',
      },

      typescript: {
        strict: true,
        vueShim: true,
      },

      vueRouterMode: 'history',

      vitePlugins: [
        [
          '@intlify/unplugin-vue-i18n/vite',
          {
            ssr: ctx.modeName === 'ssr',
            include: [fileURLToPath(new URL('./src/i18n', import.meta.url))],
          },
        ],
        [
          'vite-plugin-checker',
          {
            vueTsc: true,
            eslint: {
              lintCommand: 'eslint -c ./eslint.config.js "./src*/**/*.{ts,js,mjs,cjs,vue}"',
              useFlatConfig: true,
            },
          },
          { server: false },
        ],
      ],
    },

    devServer: {
      open: false,
      port: 9100,
    },

    framework: {
      config: {
        dark: 'auto',
        brand: {
          primary: '#7A4CE0',
          secondary: '#565C86',
          accent: '#FF7A59',
        },
      },
      iconSet: 'mdi-v7',
      lang: 'pt-BR',
      plugins: ['Notify', 'Dialog', 'Dark'],
    },

    animations: [],
  };
});

import { defineBoot } from '#q-app/wrappers';
import { createI18n } from 'vue-i18n';
import messages from 'src/i18n';

export type MessageLanguages = keyof typeof messages;
export type MessageSchema = (typeof messages)['pt-BR'];

/* eslint-disable @typescript-eslint/no-empty-object-type */
declare module 'vue-i18n' {
  export interface DefineLocaleMessage extends MessageSchema {}
}
/* eslint-enable @typescript-eslint/no-empty-object-type */

export const i18n = createI18n<{ message: MessageSchema }, MessageLanguages>({
  locale: navigator.language?.startsWith('en') ? 'en-US' : 'pt-BR',
  fallbackLocale: 'pt-BR',
  legacy: false,
  messages,
});

export default defineBoot(({ app }) => {
  app.use(i18n);
});

import { createContext, createMemo, createSignal, useContext } from 'solid-js';
import type { JSX, ParentProps } from 'solid-js';
import * as i18n from '@solid-primitives/i18n';
import { dict as enDict } from './en';

const dictionaries = {
  en: enDict,
};

export type Locale = keyof typeof dictionaries;

const createI18nStore = () => {
  const [locale, setLocale] = createSignal<Locale>('en');
  const dict = createMemo(() => i18n.flatten(dictionaries[locale()]));
  const t = i18n.translator(dict, i18n.resolveTemplate);
  return { locale, setLocale, t };
};

type I18nContextValue = ReturnType<typeof createI18nStore>;
const I18nContext = createContext<I18nContextValue>();

export function I18nProvider(props: ParentProps): JSX.Element {
  const store = createI18nStore();

  return <I18nContext.Provider value={store}>{props.children}</I18nContext.Provider>;
}

export function useI18n() {
  const context = useContext(I18nContext);
  if (!context) {
    throw new Error('useI18n must be used within I18nProvider.');
  }
  return context;
}

import { createI18n } from 'vue-i18n'
import type { Language } from '@/models/option.model'
import en from './locales/en.json'
import fa from './locales/fa.json'

// Only fa/en have real translations right now -- the other five codes in
// option.model.ts's Language type (ar/ru/es/it/pt) are a future phase, once
// the language list itself moves to a DB-backed table (per instruction,
// deferred for now). SUPPORTED_LANGUAGES is what Settings -> General
// actually offers in the picker; anything else falls back to English via
// fallbackLocale below rather than showing missing-key warnings everywhere.
export const SUPPORTED_LANGUAGES: Language[] = ['en', 'fa']

// Persisted the same way layout.store.ts already persists direction/theme --
// read directly here (not via the Pinia store, which isn't constructed yet
// this early) so the very first paint is already in the right language
// instead of flashing English then switching.
function getInitialLocale(): Language {
  if (typeof localStorage === 'undefined') return 'fa'
  const stored = localStorage.getItem('nafas-language') as Language | null
  return stored && SUPPORTED_LANGUAGES.includes(stored) ? stored : 'fa'
}

export const i18n = createI18n({
  legacy: false,
  locale: getInitialLocale(),
  fallbackLocale: 'en',
  messages: { en, fa },
})

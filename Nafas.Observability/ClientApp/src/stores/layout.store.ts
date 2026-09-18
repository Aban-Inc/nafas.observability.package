import { defineStore } from 'pinia';
import type { Direction, Language, Theme } from '@/models/option.model';
import { i18n } from '@/i18n';

export const useLayoutStore = defineStore('layout-store', {
  state() {
    return {

      direction: (typeof localStorage !== 'undefined' && localStorage.getItem('nafas-direction') || 'rtl') as Direction,
      theme: (typeof localStorage !== 'undefined' && localStorage.getItem('nafas-theme') || 'dark') as Theme,
      language: (typeof localStorage !== 'undefined' && localStorage.getItem('nafas-language') || 'fa') as Language,
      // In-memory only (not persisted) -- true once the router guard has
      // either found all three of the above already in localStorage, or
      // fetched them from GET /profiles/mine/settings. Prevents refetching
      // on every navigation within the same app session. See router/index.ts.
      settingsHydrated: false,
    }
  },
  actions: {
    setDirection(direction: Direction): void {
      this.direction = direction;

      /**
       * Set localstorage
       */
      if (typeof localStorage !== 'undefined') {
        localStorage.setItem('nafas-direction', this.direction);
        document.documentElement.setAttribute('dir', this.direction);
      }

      // Direction and language are two views of the same setting in this
      // package's UI (no independent "Farsi, but LTR" mode is offered
      // anywhere) -- switching one must switch the other, or the topbar's
      // language select and direction toggle silently disagree with each
      // other until the next full setLanguage/setDirection call. 'auto' has
      // no natural language to pair with, so it's left alone here.
      if (direction === 'ltr' && this.language !== 'en') {
        this.setLanguage('en');
      } else if (direction === 'rtl' && this.language !== 'fa') {
        this.setLanguage('fa');
      }
    },
    toggleDirection(): void {
      this.setDirection(this.direction == 'ltr' ? 'rtl' : 'ltr');
    },
    setTheme(theme: Theme): void {
      this.theme = theme;

      /**
       * Set localstorage
       */
      if (typeof localStorage !== 'undefined') {
        localStorage.setItem('nafas-theme', this.theme);
        document.documentElement.setAttribute('data-theme', this.theme);
      }
    },
    toggleTheme(): void {
      this.setTheme(this.theme == 'dark' ? 'light' : 'dark');
    },
    setLanguage(language: Language): void {
      this.language = language;

      if (typeof localStorage !== 'undefined') {
        localStorage.setItem('nafas-language', this.language);
      }

      // i18n/index.ts's messages only cover 'en'/'fa' today -- setting any
      // other code here is harmless, vue-i18n's fallbackLocale ('en')
      // quietly covers every key for a locale with no messages of its own.
      // The cast is because vue-i18n infers its Locale type from the two
      // registered message keys ('fa'|'en'), narrower than Language's full
      // seven-code union.
      i18n.global.locale.value = language as 'fa' | 'en';

      // Same pairing as setDirection's own comment, the other way around --
      // picking Farsi from the topbar's language select must flip direction
      // to rtl (and English to ltr), or the page ends up showing Farsi text
      // in an ltr layout (or vice versa) until the user separately remembers
      // to click the direction toggle too. Only en/fa drive this since
      // they're the only languages with a real (and opposite) direction in
      // this package today -- see SUPPORTED_LANGUAGES in i18n/index.ts.
      if (language === 'fa' && this.direction !== 'rtl') {
        this.setDirection('rtl');
      } else if (language === 'en' && this.direction !== 'ltr') {
        this.setDirection('ltr');
      }
    },
    setSettingsHydrated(value: boolean): void {
      this.settingsHydrated = value;
    }
  },
  getters: {
    getDirection(state): Direction {
      return state.direction;
    },
    getTheme(state): Theme {
      return state.theme;
    },
    getLanguage(state): Language {
      return state.language;
    },
    getSettingsHydrated(state): boolean {
      return state.settingsHydrated;
    }
  }
})

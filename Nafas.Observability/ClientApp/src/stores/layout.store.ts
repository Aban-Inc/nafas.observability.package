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

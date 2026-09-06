import { defineStore } from 'pinia'

export const useDashboardStore = defineStore('dashboard-store', {
  state() {
    return {
      sidebarCollapsed: false as boolean
    }
  },
  actions: {
    toggleSideBar(): void {
      this.sidebarCollapsed = !this.sidebarCollapsed;
    }
  },
  getters: {
    getSidebarCollapsed(state): boolean {
      return state.sidebarCollapsed;
    }
  }
})

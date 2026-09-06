import { defineStore } from 'pinia'

export const useLogStore = defineStore('log-store', {
  state() {
    return {
      count: 0 as number
    }
  },
  actions: {
    setCount(count: number): void {
      this.count = count;
    }
  },
  getters: {
    getCount(state): number {
      return state.count;
    }
  }
})

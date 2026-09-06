import { useToast } from 'primevue/usetoast'

export function useApiError() {
  const toast = useToast()

  function notifyError(section: string) {
    toast.add({
      severity: 'error',
      summary: 'Failed to load data',
      detail: `${section} could not be loaded.`,
      life: 5000,
    })
  }

  return { notifyError }
}

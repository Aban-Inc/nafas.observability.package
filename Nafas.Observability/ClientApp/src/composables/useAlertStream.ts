import { ref, onUnmounted } from 'vue'
import AlertService from '@/services/alert.service'

// Phase 1 of the alerting roadmap -- the SSE stream itself is served by
// no.bff.api (GET /api/alerts/stream), publishing whatever
// AlertEvaluationHostedService (no.alert.api) puts on Redis Pub/Sub
// (nafas:alerts:{tenantId}). Unlike useLogStream's raw OTLP payload, this
// one is a small JSON envelope the publisher controls end to end -- no
// parsing gymnastics needed.
export interface AlertStreamEvent {
  type: 'incident.open' | 'incident.resolved'
  ruleId: number
  ruleName: string
  detail?: string | null
  at: string
}

export function useAlertStream(onEvent: (event: AlertStreamEvent) => void) {
  const connected = ref(false)
  let eventSource: EventSource | null = null

  function connect() {
    const url = new AlertService().GetAlertStreamUrl()
    eventSource = new EventSource(url)

    eventSource.onopen = () => { connected.value = true }
    eventSource.onerror = () => { connected.value = false } // browser auto-reconnects natively, do not add manual retry logic
    eventSource.onmessage = (event) => {
      try {
        onEvent(JSON.parse(event.data) as AlertStreamEvent)
      } catch (err) {
        console.error('Failed to parse SSE alert message', err)
      }
    }
  }

  function disconnect() {
    eventSource?.close()
    eventSource = null
    connected.value = false
  }

  onUnmounted(disconnect)

  return { connect, disconnect, connected }
}

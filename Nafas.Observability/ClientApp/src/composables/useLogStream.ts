import { ref, onUnmounted } from 'vue'
import LogService from '@/services/log.service'
import type { LogEntryViewModel } from '@/models/dashboard.vm'

// The BFF's SSE stream (GET /api/logs/stream) relays raw bytes, not a clean DTO.
// Confirmed by reading the actual pipeline rather than assuming:
//   - resources/vector/vector.yaml: "No transforms: per ADR-010, Vector passes
//     the raw OTLP payload through untouched" (codec: raw_message, max_events: 1
//     — exactly one Kafka message's raw body per HTTP POST to the BFF).
//   - no.bff.api/Maps/InternalAPIMap.cs (`POST api/v1/vector`): reads the
//     request body as raw bytes and hands them straight to
//     LogStreamerService.PublishLogAsync — no deserialization anywhere.
//   - no.bff.service/LogStreamerService.cs: `StreamLog()` writes
//     `data: {message}\n\n` with that same raw payload, verbatim.
// So each SSE message is a JSON-encoded OTLP ExportLogsServiceRequest
// (resourceLogs[].scopeLogs[].logRecords[]) — the same shape a client posts
// to the log ingestion API — NOT the flat LogEntryViewModel shape SearchLogs()
// returns. Verified against test/no.test.agent/Program.cs's SendLogAsync,
// the one thing in this repo that actually produces these payloads today.
interface OtlpLogRecord {
  timeUnixNano?: string
  severityText?: string
  body?: { stringValue?: string }
}
interface OtlpResourceLogs {
  resource?: { attributes?: { key: string; value?: { stringValue?: string } }[] }
  scopeLogs?: { logRecords?: OtlpLogRecord[] }[]
}
interface OtlpExportLogsServiceRequest {
  resourceLogs?: OtlpResourceLogs[]
}

function extractServiceName(resource: OtlpResourceLogs['resource']): string {
  return resource?.attributes?.find(a => a.key === 'service.name')?.value?.stringValue ?? ''
}

function nanoToIso(timeUnixNano: string | undefined): string {
  if (!timeUnixNano) return new Date().toISOString()
  // Number() loses sub-millisecond precision on a 19-digit nanosecond string,
  // which is irrelevant here since the UI only ever displays millisecond resolution.
  const ms = Number(timeUnixNano) / 1_000_000
  return new Date(ms).toISOString()
}

// One SSE message can carry multiple resourceLogs/scopeLogs/logRecords (OTLP
// is inherently a batch export shape, even though today's only producer sends
// one record per message) — flatten defensively into individual entries,
// normalized to the same LogEntryViewModel shape SearchLogs() already returns
// so callers can treat live and polled entries identically.
function flattenOtlpLogs(payload: OtlpExportLogsServiceRequest): LogEntryViewModel[] {
  const entries: LogEntryViewModel[] = []
  for (const rl of payload.resourceLogs ?? []) {
    const serviceName = extractServiceName(rl.resource)
    for (const sl of rl.scopeLogs ?? []) {
      for (const record of sl.logRecords ?? []) {
        entries.push({
          timestamp: nanoToIso(record.timeUnixNano),
          severityText: record.severityText ?? '',
          serviceName,
          body: record.body?.stringValue ?? '',
          traceId: '',
          spanId: '',
        })
      }
    }
  }
  return entries
}

export function useLogStream(onMessage: (log: LogEntryViewModel) => void) {
  const connected = ref(false)
  let eventSource: EventSource | null = null

  function connect() {
    const url = new LogService().GetLogStreamUrl()
    eventSource = new EventSource(url)

    eventSource.onopen = () => { connected.value = true }
    eventSource.onerror = () => { connected.value = false } // browser auto-reconnects natively, do not add manual retry logic
    eventSource.onmessage = (event) => {
      try {
        const payload = JSON.parse(event.data) as OtlpExportLogsServiceRequest
        for (const entry of flattenOtlpLogs(payload)) {
          onMessage(entry)
        }
      } catch (err) {
        console.error('Failed to parse SSE log message', err)
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

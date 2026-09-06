export interface ErrorRateKpiViewModel {
  from: string
  to: string
  errorCount: number
}

export interface LogVolumeKpiViewModel {
  from: string
  to: string
  logCount: number
}

export interface VolumeOverTimePointViewModel {
  timestamp: string
  count: number
}

export interface VolumeOverTimeViewModel {
  from: string
  to: string
  interval: number
  data: VolumeOverTimePointViewModel[]
}

export interface ServiceErrorRateEntryViewModel {
  serviceName: string
  errorCount: number
  totalCount: number
}

export interface ErrorRateByServiceViewModel {
  from: string
  to: string
  data: ServiceErrorRateEntryViewModel[]
}

export interface MetricEndpointsKpiViewModel {
  from: string
  to: string
  healthy: number
  total: number
  scraping: number
  trend: number[]
}

export interface ActiveTracesKpiViewModel {
  from: string
  to: string
  count: number
  percentageChange: number
  trend: number[]
}

export interface LogEntryViewModel {
  timestamp: string
  severityText: string
  serviceName: string
  body: string
  traceId: string
  spanId: string
}

export interface LogSearchViewModel {
  from: string
  to: string
  page: number
  pageSize: number
  totalCount: number
  data: LogEntryViewModel[]
}

export interface MetricsOverviewKpiViewModel {
  from: string
  to: string
  cpuUsage: number
  cpuUsageChange: number
  memoryUsage: number
  memoryUsageChange: number
  requestRate: number
  requestRateChange: number
  p99Latency: number
  p99LatencyChange: number
}

export interface GaugeTrendPointViewModel {
  timestamp: string
  value: number
}

export interface MetricTrendViewModel {
  from: string
  to: string
  interval: number
  data: GaugeTrendPointViewModel[]
}

export interface ServiceOverviewItemViewModel {
  serviceName: string
  avgCpu: number
  avgMemory: number
  requestRate: number
  errorRate: number
}

export interface ServiceOverviewViewModel {
  from: string
  to: string
  data: ServiceOverviewItemViewModel[]
}

export interface TracesOverviewKpiViewModel {
  from: string
  to: string
  totalTraces: number
  totalTracesChange: number
  avgDuration: number
  avgDurationChange: number
  errorTraces: number
  errorTracesChange: number
  p95Duration: number
  p95DurationChange: number
}

export interface TraceSummaryViewModel {
  traceId: string
  timestamp: string
  serviceName: string
  spanName: string
  duration: number
  statusCode: string
}

export interface TraceSearchViewModel {
  from: string
  to: string
  page: number
  pageSize: number
  totalCount: number
  data: TraceSummaryViewModel[]
}

export interface LatencyHeatmapPointViewModel {
  timestamp: string
  p50: number
  p95: number
  p99: number
}

export interface LatencyHeatmapViewModel {
  from: string
  to: string
  data: LatencyHeatmapPointViewModel[]
}

export interface ServiceCatalogViewModel {
  from: string
  to: string
  services: string[]
}

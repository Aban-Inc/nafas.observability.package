import type { ActiveTracesKpiViewModel, LatencyHeatmapViewModel, TraceSearchViewModel, TracesOverviewKpiViewModel } from "@/models/dashboard.vm";
import { useHttpClient } from "@/composables/useHttpClient";
import type IResponseModel from "@/models/response.model";

export default class TraceService {
    private httpClientFactory = useHttpClient();

    // Embedded package, single implicit app -- no bearer token, no tenant
    // scoping. Kept as a method (not inlined at each call site) so a future
    // auth hook (see dotnet/CLAUDE.md's dashboard-auth phase) has one place
    // to plug into.
    private authHeaders(): Record<string, string> {
        return {};
    }

    public async GetActiveTracesKPI(from: string, to: string, serviceName?: string): Promise<ActiveTracesKpiViewModel | null | undefined> {
        const _response = await this.httpClientFactory.httpClient<IResponseModel<ActiveTracesKpiViewModel>>(
            `/api/traces/kpi/active/${from}/${to}`,
            this.authHeaders(),
            'GET',
            undefined,
            serviceName ? { service_name: serviceName } : undefined
        );
        return _response.content;
    }

    public async GetTracesOverviewKPI(from: string, to: string, serviceName?: string): Promise<TracesOverviewKpiViewModel | null | undefined> {
        const _response = await this.httpClientFactory.httpClient<IResponseModel<TracesOverviewKpiViewModel>>(
            `/api/traces/kpi/overview/${from}/${to}`,
            this.authHeaders(),
            'GET',
            undefined,
            serviceName ? { service_name: serviceName } : undefined
        );
        return _response.content;
    }

    public async GetLatencyHeatmap(from: string, to: string, bucketHours = 1, serviceName?: string): Promise<LatencyHeatmapViewModel | null | undefined> {
        const params: Record<string, string> = { bucket_hours: String(bucketHours) };
        if (serviceName) params.service_name = serviceName;

        const _response = await this.httpClientFactory.httpClient<IResponseModel<LatencyHeatmapViewModel>>(
            `/api/traces/charts/latency-heatmap/${from}/${to}`,
            this.authHeaders(),
            'GET',
            undefined,
            params
        );
        return _response.content;
    }

    public async SearchTraces(from: string, to: string, service: string | undefined, traceId: string | undefined, page: number, pageSize: number): Promise<TraceSearchViewModel | null | undefined> {
        const params: Record<string, string> = { page: String(page), pageSize: String(pageSize) };
        if (service) params.service = service;
        if (traceId) params.traceId = traceId;

        const _response = await this.httpClientFactory.httpClient<IResponseModel<TraceSearchViewModel>>(
            `/api/traces/search/${from}/${to}`,
            this.authHeaders(),
            'GET',
            undefined,
            params
        );
        return _response.content;
    }
}

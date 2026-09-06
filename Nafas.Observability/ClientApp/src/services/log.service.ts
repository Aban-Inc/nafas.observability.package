import type { ErrorRateByServiceViewModel, ErrorRateKpiViewModel, LogSearchViewModel, LogVolumeKpiViewModel, VolumeOverTimeViewModel } from "@/models/dashboard.vm";
import { useHttpClient } from "@/composables/useHttpClient";
import type IResponseModel from "@/models/response.model";

export default class LogService {
    private httpClientFactory = useHttpClient();

    // Embedded package, single implicit app -- no bearer token, no tenant
    // scoping (unlike the SaaS platform's BFF, which required both). Kept as
    // a method (not inlined at each call site) so a future auth hook (see
    // dotnet/CLAUDE.md's dashboard-auth phase) has one place to plug into.
    private authHeaders(): Record<string, string> {
        return {};
    }

    public async GetLogCount(): Promise<number> {
        return 0;
    }

    public async GetLogs(): Promise<[]> {
        return [];
    }

    public GetLogStreamUrl(): string {
        return this.httpClientFactory.streamUrl('/api/logs/stream', {});
    }

    public async GetErrorRateKPI(from: string, to: string, serviceName?: string): Promise<ErrorRateKpiViewModel | null | undefined> {
        const _response = await this.httpClientFactory.httpClient<IResponseModel<ErrorRateKpiViewModel>>(
            `/api/logs/kpi/error-rate/${from}/${to}`,
            this.authHeaders(),
            'GET',
            undefined,
            serviceName ? { service_name: serviceName } : undefined
        );

        return _response.content;
    }

    public async GetLogVolumeKPI(from: string, to: string, serviceName?: string): Promise<LogVolumeKpiViewModel | null | undefined> {
        const _response = await this.httpClientFactory.httpClient<IResponseModel<LogVolumeKpiViewModel>>(
            `/api/logs/kpi/log-volume/${from}/${to}`,
            this.authHeaders(),
            'GET',
            undefined,
            serviceName ? { service_name: serviceName } : undefined
        );
        return _response.content;
    }

    public async GetVolumeOverTime(from: string, to: string, interval: number, serviceName?: string): Promise<VolumeOverTimeViewModel | null | undefined> {
        const _response = await this.httpClientFactory.httpClient<IResponseModel<VolumeOverTimeViewModel>>(
            `/api/logs/charts/volume-over-time/${from}/${to}/${interval}`,
            this.authHeaders(),
            'GET',
            undefined,
            serviceName ? { service_name: serviceName } : undefined
        );
        return _response.content;
    }

    public async GetErrorRateByService(from: string, to: string): Promise<ErrorRateByServiceViewModel | null | undefined> {
        const _response = await this.httpClientFactory.httpClient<IResponseModel<ErrorRateByServiceViewModel>>(
            `/api/logs/kpi/error-rate/by-service/${from}/${to}`,
            this.authHeaders(),
            'GET',
            undefined,
            undefined
        );
        return _response.content;
    }

    public async SearchLogs(from: string, to: string, level: string | undefined, service: string | undefined, search: string | undefined, page: number, pageSize: number): Promise<LogSearchViewModel | null | undefined> {
        const params: Record<string, string> = { page: String(page), pageSize: String(pageSize) };
        if (level) params.level = level;
        if (service) params.service = service;
        if (search) params.search = search;

        const _response = await this.httpClientFactory.httpClient<IResponseModel<LogSearchViewModel>>(
            `/api/logs/search/${from}/${to}`,
            this.authHeaders(),
            'GET',
            undefined,
            params
        );
        return _response.content;
    }
}

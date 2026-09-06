import type { MetricEndpointsKpiViewModel, MetricsOverviewKpiViewModel, MetricTrendViewModel, ServiceOverviewViewModel } from "@/models/dashboard.vm";
import { useHttpClient } from "@/composables/useHttpClient";
import type IResponseModel from "@/models/response.model";

export default class MetricService {
    private httpClientFactory = useHttpClient();

    // Embedded package, single implicit app -- no bearer token, no tenant
    // scoping. Kept as a method (not inlined at each call site) so a future
    // auth hook (see dotnet/CLAUDE.md's dashboard-auth phase) has one place
    // to plug into.
    private authHeaders(): Record<string, string> {
        return {};
    }

    public async GetMetricEndpointsKPI(from: string, to: string, serviceName?: string): Promise<MetricEndpointsKpiViewModel | null | undefined> {
        const _response = await this.httpClientFactory.httpClient<IResponseModel<MetricEndpointsKpiViewModel>>(
            `/api/metrics/kpi/endpoints/${from}/${to}`,
            this.authHeaders(),
            'GET',
            undefined,
            serviceName ? { service_name: serviceName } : undefined
        );
        return _response.content;
    }

    public async GetMetricsOverviewKPI(from: string, to: string, serviceName?: string): Promise<MetricsOverviewKpiViewModel | null | undefined> {
        const _response = await this.httpClientFactory.httpClient<IResponseModel<MetricsOverviewKpiViewModel>>(
            `/api/metrics/kpi/overview/${from}/${to}`,
            this.authHeaders(),
            'GET',
            undefined,
            serviceName ? { service_name: serviceName } : undefined
        );
        return _response.content;
    }

    public async GetCpuUsageTrend(from: string, to: string, interval: number, serviceName?: string): Promise<MetricTrendViewModel | null | undefined> {
        const _response = await this.httpClientFactory.httpClient<IResponseModel<MetricTrendViewModel>>(
            `/api/metrics/charts/cpu-usage/${from}/${to}/${interval}`,
            this.authHeaders(),
            'GET',
            undefined,
            serviceName ? { service_name: serviceName } : undefined
        );
        return _response.content;
    }

    public async GetMemoryUsageTrend(from: string, to: string, interval: number, serviceName?: string): Promise<MetricTrendViewModel | null | undefined> {
        const _response = await this.httpClientFactory.httpClient<IResponseModel<MetricTrendViewModel>>(
            `/api/metrics/charts/memory-usage/${from}/${to}/${interval}`,
            this.authHeaders(),
            'GET',
            undefined,
            serviceName ? { service_name: serviceName } : undefined
        );
        return _response.content;
    }

    public async GetServiceOverviewKPI(from: string, to: string): Promise<ServiceOverviewViewModel | null | undefined> {
        const _response = await this.httpClientFactory.httpClient<IResponseModel<ServiceOverviewViewModel>>(
            `/api/metrics/kpi/service-overview/${from}/${to}`,
            this.authHeaders(),
            'GET',
            undefined,
            undefined
        );
        return _response.content;
    }
}

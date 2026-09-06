import { useHttpClient } from "@/composables/useHttpClient"
import type {
    AlertIncidentViewModel,
    AlertRuleViewModel,
    CreateAlertRuleRequest,
    UpdateAlertRuleRequest
} from "@/models/alert.vm"
import type IResponseModel from "@/models/response.model"

export default class AlertService {
    private httpClientFactory = useHttpClient()

    public GetAlertStreamUrl(): string {
        return this.httpClientFactory.streamUrl('/api/alerts/stream', {});
    }

    public async ListRules(): Promise<AlertRuleViewModel[]> {
        const response = await this.httpClientFactory.httpClient<IResponseModel<AlertRuleViewModel[]>>(
            '/api/v1/rules',
            this.authHeaders(),
            'GET',
            undefined,
            undefined
        );
        return response.content ?? [];
    }

    public async CreateRule(request: CreateAlertRuleRequest): Promise<AlertRuleViewModel> {
        const response = await this.httpClientFactory.httpClient<IResponseModel<AlertRuleViewModel>>(
            '/api/v1/rules',
            this.authHeaders(),
            'POST',
            request,
            undefined
        );
        return response.content as AlertRuleViewModel;
    }

    public async UpdateRule(id: number, request: UpdateAlertRuleRequest): Promise<AlertRuleViewModel> {
        const response = await this.httpClientFactory.httpClient<IResponseModel<AlertRuleViewModel>>(
            `/api/v1/rules/${id}`,
            this.authHeaders(),
            'PUT',
            request,
            undefined
        );
        return response.content as AlertRuleViewModel;
    }

    public async DeleteRule(id: number): Promise<void> {
        await this.httpClientFactory.httpClient<IResponseModel<null>>(
            `/api/v1/rules/${id}`,
            this.authHeaders(),
            'DELETE',
            undefined,
            undefined
        );
    }

    public async ListIncidents(): Promise<AlertIncidentViewModel[]> {
        const response = await this.httpClientFactory.httpClient<IResponseModel<AlertIncidentViewModel[]>>(
            '/api/v1/incidents',
            this.authHeaders(),
            'GET',
            undefined,
            undefined
        );
        return response.content ?? [];
    }

    // Embedded package, single implicit app -- no bearer token, no tenant
    // scoping. Kept as a method (not inlined at each call site) so a future
    // auth hook (see dotnet/CLAUDE.md's dashboard-auth phase) has one place
    // to plug into.
    private authHeaders(): Record<string, string> {
        return {};
    }
}

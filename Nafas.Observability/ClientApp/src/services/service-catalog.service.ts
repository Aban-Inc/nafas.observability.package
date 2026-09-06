import type { ServiceCatalogViewModel } from "@/models/dashboard.vm";
import { useHttpClient } from "@/composables/useHttpClient";
import type IResponseModel from "@/models/response.model";

export default class ServiceCatalogService {
    private httpClientFactory = useHttpClient();

    // Embedded package, single implicit app -- no bearer token, no tenant
    // scoping. Kept as a method (not inlined at each call site) so a future
    // auth hook (see dotnet/CLAUDE.md's dashboard-auth phase) has one place
    // to plug into.
    private authHeaders(): Record<string, string> {
        return {};
    }

    public async ListServices(from: string, to: string): Promise<ServiceCatalogViewModel | null | undefined> {
        const _response = await this.httpClientFactory.httpClient<IResponseModel<ServiceCatalogViewModel>>(
            `/api/services/list/${from}/${to}`,
            this.authHeaders(),
            'GET',
            undefined,
            undefined
        );
        return _response.content;
    }
}

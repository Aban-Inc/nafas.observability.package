import { ofetch } from 'ofetch'
import type { HttpVerb } from "@/models/option.model"
import { getBasePath } from "@/utils/serviceBaseUrl"

// No auth-refresh/401-retry branch here (unlike the SaaS platform's version
// this is derived from) -- there's no login/session/token concept in this
// package at all. If/when a dashboard-auth hook is added (see
// dotnet/CLAUDE.md's roadmap), it plugs in here.
export function useHttpClient() {
  const baseUrl = getBasePath()

  async function httpClient<R>(resource: string, headers: Record<string, string>, method: HttpVerb, payload: {} | undefined, params: Record<string, string> | undefined): Promise<R> {
    return await ofetch<R>(`${baseUrl}${resource}`, {
      headers: headers,
      method: method,
      body: payload,
      query: params
    })
  }

  function streamUrl(path: string, params: Record<string, string>) {
    const query = Object.entries(params)
      .map(([key, value]) => `${encodeURIComponent(key)}=${encodeURIComponent(value)}`)
      .join('&')
    return query ? `${baseUrl}${path}?${query}` : `${baseUrl}${path}`
  }

  return { baseUrl, httpClient, streamUrl }
}

import { describe, expect, it, vi } from "vitest";
import { buildEvidenceApiUrl, createEvidenceApi, EvidenceApiError, getEvidenceApiConfig } from "./evidence-api";

describe("evidence API client", () => {
  it("uses the local Demo API only outside production when no URL is configured", () => {
    expect(getEvidenceApiConfig({ NODE_ENV: "development" })).toMatchObject({
      baseUrl: "http://localhost:5271",
      provider: "Demo",
    });
    expect(getEvidenceApiConfig({ NODE_ENV: "production" }).baseUrl).toBeNull();
  });

  it("builds bounded asset URLs from the configured API", () => {
    expect(buildEvidenceApiUrl("/api/v1/assets?page=1&pageSize=20", { baseUrl: "http://api.test", provider: "Demo" }))
      .toBe("http://api.test/api/v1/assets?page=1&pageSize=20");
  });

  it("maps an asset page without inventing fields", async () => {
    const fetcher = vi.fn().mockResolvedValue(new Response(JSON.stringify({
      items: [{ id: "asset-1", slug: "demo-asset", name: "Demo asset", assetType: "illustrative", shortSummary: null, claimCount: 1, sourceCount: 1 }],
      page: 1,
      pageSize: 20,
      hasNextPage: false,
    }), { status: 200, headers: { "Content-Type": "application/json" } }));
    const api = createEvidenceApi({ baseUrl: "http://api.test", provider: "Demo" }, fetcher);

    await expect(api.listAssets()).resolves.toMatchObject({ page: 1, items: [{ slug: "demo-asset", shortSummary: null }] });
    expect(fetcher).toHaveBeenCalledWith("http://api.test/api/v1/assets?page=1&pageSize=20", expect.anything());
  });

  it("returns null for a provider 404 and throws for other failures", async () => {
    const notFoundFetcher = vi.fn().mockResolvedValue(new Response("", { status: 404 }));
    const api = createEvidenceApi({ baseUrl: "http://api.test", provider: "Demo" }, notFoundFetcher);
    await expect(api.getClaim("missing")).resolves.toBeNull();

    const failureFetcher = vi.fn().mockResolvedValue(new Response("", { status: 503 }));
    const failureApi = createEvidenceApi({ baseUrl: "http://api.test", provider: "Demo" }, failureFetcher);
    await expect(failureApi.listAssets()).rejects.toBeInstanceOf(EvidenceApiError);
  });
});

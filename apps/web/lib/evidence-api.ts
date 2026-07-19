import type {
  PublicEvidenceAsset,
  PublicEvidenceAssetDetail,
  PublicEvidenceClaim,
  PublicEvidencePage,
} from "@longevity/shared";

const LOCAL_API_BASE_URL = "http://localhost:5271";

export class EvidenceApiError extends Error {
  constructor(
    message: string,
    public readonly status?: number,
  ) {
    super(message);
    this.name = "EvidenceApiError";
  }
}

export interface EvidenceApiConfig {
  baseUrl: string | null;
  provider: string;
}

export function getEvidenceApiConfig(
  env: NodeJS.ProcessEnv = process.env,
): EvidenceApiConfig {
  const configuredBaseUrl = env.EVIDENCE_API_BASE_URL?.trim();
  const baseUrl = configuredBaseUrl || (env.NODE_ENV === "production" ? null : LOCAL_API_BASE_URL);

  return {
    baseUrl: baseUrl?.replace(/\/$/, "") ?? null,
    provider: env.EVIDENCE_API_PROVIDER?.trim() || "Demo",
  };
}

export function buildEvidenceApiUrl(path: string, config = getEvidenceApiConfig()): string {
  if (!config.baseUrl) {
    throw new EvidenceApiError(
      "Evidence API is not configured. Set EVIDENCE_API_BASE_URL before starting the web app.",
    );
  }

  return `${config.baseUrl}${path.startsWith("/") ? path : `/${path}`}`;
}

type EvidenceFetcher = (input: RequestInfo | URL, init?: RequestInit) => Promise<Response>;

export function createEvidenceApi(
  config = getEvidenceApiConfig(),
  fetcher: EvidenceFetcher = fetch,
) {
  async function request<T>(path: string): Promise<T> {
    const response = await fetcher(buildEvidenceApiUrl(path, config), {
      cache: "no-store",
      headers: { Accept: "application/json" },
    });

    if (!response.ok) {
      throw new EvidenceApiError(
        `Evidence API request failed with status ${response.status}.`,
        response.status,
      );
    }

    return (await response.json()) as T;
  }

  return {
    config,
    listAssets(page = 1, pageSize = 20) {
      return request<PublicEvidencePage<PublicEvidenceAsset>>(
        `/api/v1/assets?page=${page}&pageSize=${pageSize}`,
      );
    },
    getAsset(slug: string) {
      return request<PublicEvidenceAssetDetail>(`/api/v1/assets/${encodeURIComponent(slug)}`).catch((error: unknown) => {
        if (error instanceof EvidenceApiError && error.status === 404) return null;
        throw error;
      });
    },
    getClaim(claimId: string) {
      return request<PublicEvidenceClaim>(`/api/v1/claims/${encodeURIComponent(claimId)}`).catch((error: unknown) => {
        if (error instanceof EvidenceApiError && error.status === 404) return null;
        throw error;
      });
    },
  };
}

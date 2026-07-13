export const APP_NAME = "Longevity Intelligence";
export const APP_TAGLINE = "Structured intelligence for longevity.";
export const HELLO_WORLD_MESSAGE = "One evidence-first foundation, ready for every screen.";

export type AppPlatform = "web" | "desktop" | "ios" | "android";

export interface HelloWorldPayload {
  appName: typeof APP_NAME;
  tagline: typeof APP_TAGLINE;
  message: typeof HELLO_WORLD_MESSAGE;
  platform: AppPlatform;
}

export function createHelloWorldPayload(platform: AppPlatform): HelloWorldPayload {
  return {
    appName: APP_NAME,
    tagline: APP_TAGLINE,
    message: HELLO_WORLD_MESSAGE,
    platform,
  };
}

export interface PublicEvidenceAsset {
  id: string;
  slug: string;
  name: string;
  assetType: string;
  shortSummary: string | null;
  claimCount: number;
  sourceCount: number;
}

export interface PublicEvidenceSource {
  id: string;
  sourceType: string;
  title: string;
  url: string | null;
  publicationName: string | null;
  publishedDate: string | null;
  doi: string | null;
  pmid: string | null;
  trialId: string | null;
  qualityScore: number | null;
}

export interface PublicEvidenceItem {
  id: string;
  sourceId: string;
  evidenceDirection: string;
  evidenceLevel: string;
  population: string | null;
  outcomeMeasured: string | null;
  effectSummary: string | null;
  limitations: string | null;
  relevanceScore: number | null;
  source: PublicEvidenceSource;
}

export interface PublicEvidenceClaim {
  id: string;
  assetId: string;
  claimText: string;
  claimType: string | null;
  targetSystem: string | null;
  evidenceScore: number | null;
  hypeScore: number | null;
  riskScore: number | null;
  plainEnglishVerdict: string | null;
  evidenceCount: number;
  evidence: PublicEvidenceItem[];
}

export interface PublicEvidenceAssetDetail {
  asset: PublicEvidenceAsset;
  claims: PublicEvidenceClaim[];
}

export interface PublicEvidencePage<T> {
  items: T[];
  page: number;
  pageSize: number;
  hasNextPage: boolean;
}

import Link from "next/link";
import { notFound } from "next/navigation";
import { ClaimCard, DemoBanner, ApiUnavailable, PageHeader, formatLabel } from "../../../components/evidence-ui";
import { createEvidenceApi, EvidenceApiError } from "../../../lib/evidence-api";

export const dynamic = "force-dynamic";

export default async function AssetDetailPage({ params }: { params: Promise<{ slug: string }> }) {
  const { slug } = await params;
  const api = createEvidenceApi();

  try {
    const detail = await api.getAsset(slug);
    if (!detail) notFound();

    return (
      <div className="content-stack">
        <DemoBanner provider={api.config.provider} />
        <Link className="back-link" href="/assets">← Back to assets</Link>
        <PageHeader
          eyebrow={formatLabel(detail.asset.assetType)}
          title={detail.asset.name}
          description={detail.asset.shortSummary || "No summary provided by the evidence registry."}
        />
        <div className="registry-stats" aria-label="Asset registry counts">
          <div><strong>{detail.asset.claimCount}</strong><span>Claims</span></div>
          <div><strong>{detail.asset.sourceCount}</strong><span>Sources</span></div>
        </div>
        <section className="section-block" aria-labelledby="claims-heading">
          <div className="section-heading">
            <div><div className="eyebrow">Claim registry</div><h2 id="claims-heading">What the record says</h2></div>
            <p>Scores and verdicts are registry fields. They are not medical recommendations.</p>
          </div>
          {detail.claims.length ? (
            <div className="claim-list">{detail.claims.map((claim) => <ClaimCard claim={claim} key={claim.id} />)}</div>
          ) : (
            <div className="state-card"><p>No claims are attached to this asset.</p></div>
          )}
        </section>
      </div>
    );
  } catch (error) {
    if (error instanceof EvidenceApiError && error.status === 404) notFound();
    return <ApiUnavailable detail={error instanceof EvidenceApiError ? error.message : undefined} />;
  }
}

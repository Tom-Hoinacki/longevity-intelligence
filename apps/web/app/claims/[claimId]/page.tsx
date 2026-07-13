import Link from "next/link";
import { notFound } from "next/navigation";
import { ApiUnavailable, DemoBanner, EvidenceCard, PageHeader, ScoreGrid, formatLabel } from "../../../components/evidence-ui";
import { createEvidenceApi, EvidenceApiError } from "../../../lib/evidence-api";

export const dynamic = "force-dynamic";

export default async function ClaimDetailPage({ params }: { params: Promise<{ claimId: string }> }) {
  const { claimId } = await params;
  const api = createEvidenceApi();

  try {
    const claim = await api.getClaim(claimId);
    if (!claim) notFound();

    return (
      <div className="content-stack">
        <DemoBanner provider={api.config.provider} />
        <Link className="back-link" href="/assets">← Back to assets</Link>
        <PageHeader
          eyebrow={formatLabel(claim.claimType)}
          title="Claim detail"
          description="Inspect the evidence items and source provenance attached to this registry claim."
        />
        <article className="claim-detail">
          <div className="claim-card-topline">
            <span className="card-kicker">{claim.targetSystem ? `Target: ${formatLabel(claim.targetSystem)}` : "Target not provided"}</span>
            {claim.plainEnglishVerdict && <span className="verdict">{claim.plainEnglishVerdict}</span>}
          </div>
          <h2>{claim.claimText}</h2>
          <ScoreGrid claim={claim} />
          <p className="score-note">Scores are displayed exactly as supplied by the public evidence registry. Missing values are intentionally not filled in.</p>
        </article>
        <section className="section-block" aria-labelledby="evidence-heading">
          <div className="section-heading">
            <div><div className="eyebrow">Provenance</div><h2 id="evidence-heading">Linked evidence</h2></div>
            <p>{claim.evidenceCount} evidence item{claim.evidenceCount === 1 ? "" : "s"} linked to this claim.</p>
          </div>
          {claim.evidence.length ? (
            <div className="evidence-list">{claim.evidence.map((item) => <EvidenceCard item={item} key={item.id} />)}</div>
          ) : (
            <div className="state-card"><p>No evidence items are attached to this claim.</p></div>
          )}
        </section>
      </div>
    );
  } catch (error) {
    if (error instanceof EvidenceApiError && error.status === 404) notFound();
    return <ApiUnavailable detail={error instanceof EvidenceApiError ? error.message : undefined} />;
  }
}

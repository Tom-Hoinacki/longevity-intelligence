import Link from "next/link";
import type {
  PublicEvidenceAsset,
  PublicEvidenceClaim,
  PublicEvidenceItem,
  PublicEvidenceSource,
} from "@longevity/shared";

export function formatLabel(value: string | null | undefined) {
  if (!value) return "Not provided";
  return value
    .replace(/[_-]+/g, " ")
    .replace(/\b\w/g, (character) => character.toUpperCase());
}

export function formatDate(value: string | null) {
  if (!value) return "Date not provided";
  const date = new Date(value);
  return Number.isNaN(date.valueOf())
    ? value
    : new Intl.DateTimeFormat("en", { year: "numeric", month: "short", day: "numeric" }).format(date);
}

export function formatScore(value: number | null) {
  return value === null ? "Not available" : `${value}`;
}

export function DemoBanner({ provider }: { provider: string }) {
  const isDemo = provider.toLowerCase() === "demo";

  return (
    <aside className={`provider-banner ${isDemo ? "provider-banner-demo" : ""}`}>
      <span className="provider-dot" aria-hidden="true" />
      <div>
        <strong>{isDemo ? "Illustrative demo data" : `${provider} evidence provider`}</strong>
        <p>
          {isDemo
            ? "This local provider is for interface development and is not medical evidence or a recommendation."
            : "Displayed records come from the configured public evidence provider. Missing values remain unavailable."}
        </p>
      </div>
    </aside>
  );
}

export function PageHeader({ eyebrow, title, description }: { eyebrow: string; title: string; description: string }) {
  return (
    <header className="page-header">
      <div className="eyebrow">{eyebrow}</div>
      <h1>{title}</h1>
      <p>{description}</p>
    </header>
  );
}

export function AssetCard({ asset }: { asset: PublicEvidenceAsset }) {
  return (
    <Link className="asset-card" href={`/assets/${asset.slug}`}>
      <div className="card-kicker">{formatLabel(asset.assetType)}</div>
      <h2>{asset.name}</h2>
      <p>{asset.shortSummary || "No summary provided by the evidence registry."}</p>
      <div className="card-footer">
        <span>{asset.claimCount} claims</span>
        <span>{asset.sourceCount} sources</span>
        <span className="arrow" aria-hidden="true">↗</span>
      </div>
    </Link>
  );
}

export function ScoreGrid({ claim }: { claim: PublicEvidenceClaim }) {
  const scores = [
    ["Evidence score", claim.evidenceScore],
    ["Hype score", claim.hypeScore],
    ["Risk score", claim.riskScore],
  ] as const;

  return (
    <div className="score-grid" aria-label="Registry scores">
      {scores.map(([label, value]) => (
        <div className="score" key={label}>
          <span>{label}</span>
          <strong>{formatScore(value)}</strong>
        </div>
      ))}
    </div>
  );
}

export function ClaimCard({ claim }: { claim: PublicEvidenceClaim }) {
  return (
    <article className="claim-card">
      <div className="claim-card-topline">
        <span className="card-kicker">{formatLabel(claim.claimType)}</span>
        {claim.plainEnglishVerdict && <span className="verdict">{claim.plainEnglishVerdict}</span>}
      </div>
      <h3>{claim.claimText}</h3>
      <p className="claim-context">
        {claim.targetSystem ? `Target system: ${formatLabel(claim.targetSystem)} · ` : ""}
        {claim.evidenceCount} linked evidence item{claim.evidenceCount === 1 ? "" : "s"}
      </p>
      <ScoreGrid claim={claim} />
      <Link className="text-link" href={`/claims/${claim.id}`}>Inspect claim evidence <span aria-hidden="true">→</span></Link>
    </article>
  );
}

function SourceIdentifiers({ source }: { source: PublicEvidenceSource }) {
  const identifiers = [
    ["DOI", source.doi],
    ["PMID", source.pmid],
    ["Trial ID", source.trialId],
  ].filter(([, value]) => value);

  if (!identifiers.length) return null;

  return (
    <dl className="source-identifiers">
      {identifiers.map(([label, value]) => (
        <div key={label}>
          <dt>{label}</dt>
          <dd>{value}</dd>
        </div>
      ))}
    </dl>
  );
}

export function SourceCard({ source }: { source: PublicEvidenceSource }) {
  return (
    <article className="source-card">
      <div className="card-kicker">{formatLabel(source.sourceType)}</div>
      <h3>{source.title}</h3>
      <p className="source-publication">
        {source.publicationName || "Publication not provided"} · {formatDate(source.publishedDate)}
      </p>
      <SourceIdentifiers source={source} />
      {source.url && (
        <a className="text-link" href={source.url} target="_blank" rel="noreferrer">
          Open source <span aria-hidden="true">↗</span>
        </a>
      )}
    </article>
  );
}

export function EvidenceCard({ item }: { item: PublicEvidenceItem }) {
  return (
    <article className="evidence-card">
      <div className="evidence-card-topline">
        <span className="card-kicker">{formatLabel(item.evidenceLevel)}</span>
        <span className="evidence-direction">{formatLabel(item.evidenceDirection)}</span>
      </div>
      <h3>{item.outcomeMeasured || "Outcome not provided"}</h3>
      <dl className="evidence-details">
        <div><dt>Population</dt><dd>{item.population || "Not provided"}</dd></div>
        <div><dt>Effect summary</dt><dd>{item.effectSummary || "Not provided"}</dd></div>
        <div><dt>Limitations</dt><dd>{item.limitations || "Not provided"}</dd></div>
      </dl>
      <SourceCard source={item.source} />
    </article>
  );
}

export function ApiUnavailable({ detail }: { detail?: string }) {
  return (
    <section className="state-card" role="alert">
      <div className="eyebrow">Evidence API unavailable</div>
      <h2>Connect the configured public provider to explore records.</h2>
      <p>{detail || "Start the .NET API with the Demo provider, then refresh this page."}</p>
      <code>EVIDENCE_API_BASE_URL=http://localhost:5271</code>
    </section>
  );
}

export function EmptyState({ message }: { message: string }) {
  return (
    <section className="state-card">
      <div className="eyebrow">No records</div>
      <h2>Nothing is available in this registry view yet.</h2>
      <p>{message}</p>
    </section>
  );
}

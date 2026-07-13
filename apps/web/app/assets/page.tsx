import { DemoBanner, ApiUnavailable, AssetCard, EmptyState, PageHeader } from "../../components/evidence-ui";
import { createEvidenceApi, EvidenceApiError } from "../../lib/evidence-api";

export const dynamic = "force-dynamic";

export default async function AssetsPage() {
  const api = createEvidenceApi();

  try {
    const page = await api.listAssets(1, 20);

    return (
      <div className="content-stack">
        <DemoBanner provider={api.config.provider} />
        <PageHeader
          eyebrow="Public evidence registry"
          title="Explore the evidence graph."
          description="Start with an asset, follow its claims, and inspect the evidence and sources attached to each record."
        />
        {page.items.length ? (
          <section className="asset-grid" aria-label="Evidence assets">
            {page.items.map((asset) => <AssetCard asset={asset} key={asset.id} />)}
          </section>
        ) : (
          <EmptyState message="The configured public provider returned no assets for this page." />
        )}
        <div className="pagination-note">Showing page {page.page} · {page.items.length} of up to {page.pageSize} records</div>
      </div>
    );
  } catch (error) {
    return <ApiUnavailable detail={error instanceof EvidenceApiError ? error.message : undefined} />;
  }
}

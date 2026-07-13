import Link from "next/link";
import { APP_NAME, APP_TAGLINE } from "@longevity/shared";
import { DemoBanner } from "../components/evidence-ui";
import { getEvidenceApiConfig } from "../lib/evidence-api";

export default function Home() {
  return (
    <div className="home-grid">
      <section className="home-hero" aria-labelledby="home-title">
        <div className="eyebrow">Evidence intelligence</div>
        <h1 id="home-title">Make longevity evidence easier to understand.</h1>
        <p>{APP_TAGLINE} Follow each record from asset to claim to source-backed evidence.</p>
        <Link className="primary-button" href="/assets">Explore the evidence registry <span aria-hidden="true">→</span></Link>
      </section>
      <aside className="home-aside">
        <DemoBanner provider={getEvidenceApiConfig().provider} />
        <div className="home-note"><span className="eyebrow">The model</span><strong>Asset → Claim → Source → Evidence</strong><p>{APP_NAME} keeps provenance visible so uncertainty and limitations stay part of the record.</p></div>
      </aside>
    </div>
  );
}

import Link from "next/link";

export default function NotFound() {
  return (
    <section className="state-card">
      <div className="eyebrow">404 · Registry</div>
      <h1>That record is not available.</h1>
      <p>The public evidence provider did not return a record for this address.</p>
      <Link className="primary-button" href="/assets">Return to assets</Link>
    </section>
  );
}

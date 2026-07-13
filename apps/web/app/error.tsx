"use client";

export default function Error({ reset }: { error: Error & { digest?: string }; reset: () => void }) {
  return (
    <section className="state-card" role="alert">
      <div className="eyebrow">Unexpected registry error</div>
      <h1>We could not load this evidence view.</h1>
      <p>Try again, or check that the configured public evidence API is running.</p>
      <button className="primary-button" onClick={() => reset()}>Try again</button>
    </section>
  );
}

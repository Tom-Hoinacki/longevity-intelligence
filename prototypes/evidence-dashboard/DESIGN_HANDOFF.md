# Design handoff

## Information architecture

The workspace has a global product header, asset identity, tab navigation, an evidence-confidence summary, claim ledger, and provenance trail. Selecting a claim opens a focused detail view and returns to the overview.

## Components and interactions

`index.html` owns semantic landmarks and layout; `data.js` is the deterministic asset/claim/source fixture; `app.js` renders cards, verdict filters, search, keyboard shortcut `/`, claim detail, and share feedback; `styles.css` owns tokens, responsive layout, meters, and focus-friendly controls. The future React version can map these to `AppShell`, `AssetHeader`, `EvidenceSummary`, `ClaimLedger`, `ClaimDetail`, and `SourceCard`.

## Responsive and accessibility decisions

The two-column summary becomes one column below 760px, source cards become a compact grid, and navigation remains horizontally scrollable. Semantic header/nav/main/footer landmarks, labelled search, keyboard-operable claim cards, visible skip link, focusable controls, readable contrast, and status announcements are included. This prototype uses no motion that requires a reduced-motion override.

## Mock data and limitations

The fixture models an asset, claims, source counts, supportive/contradictory links, verdicts, confidence scores, and provenance metadata. Every item is fictional demo content; it is not a source of medical guidance and the score is not clinically meaningful. There is no persistence, authentication, backend API, real URL sharing, or automated axe integration yet.

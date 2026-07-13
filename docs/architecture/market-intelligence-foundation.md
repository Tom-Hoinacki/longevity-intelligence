# Market intelligence foundation

This backend foundation adds public, source-backed market data for longevity-related assets. It is educational market intelligence, not medical advice.

## Asset versus offering

An evidence asset is the intervention, category, diagnostic, supplement, device, service category, or other subject about which evidence claims are made. A commercial offering is a purchasable package, SKU, subscription, service, membership, or branded offer connected to an asset. An asset is not automatically an offering, and multiple providers can sell different offerings related to the same asset.

## Provider model

Providers represent companies, clinics, labs, pharmacies, retailers, marketplaces, manufacturers, subscription services, or other sellers. The model intentionally excludes partner status, sponsorship flags, commissions, referral tracking, and affiliate credentials.

## Price observations

Prices are append-only historical observations with amount, uppercase three-letter currency code, pricing basis, optional interval or quantity metadata, market region, observed timestamp, source URL, source label, and created timestamp. Prices may be stale. The system does not normalize currencies or calculate monthly costs unless source data later supports that explicitly.

## Availability observations

Availability observations are append-only records of whether an offering appeared available, out of stock, waitlisted, unavailable, discontinued, or unknown in a region at an observed timestamp. Availability is not inferred from the existence of a price.

## Provenance requirements

Every price and availability observation requires an offering, timestamp, source URL, and created timestamp. Storing a source URL does not mean the source was verified.

## Public API routes

Read-only routes are versioned under `/api/v1`:

- `GET /api/v1/assets/{assetSlug}/offerings`
- `GET /api/v1/offerings/{offeringId}`
- `GET /api/v1/offerings/{offeringId}/prices`
- `GET /api/v1/offerings/{offeringId}/availability`

History endpoints support bounded `limit`, `cursor`, `startDate`, and `endDate` filters with deterministic `observed_at desc, id desc` ordering.

## Demo and Postgres modes

Demo mode is explicit and uses obviously fictional illustrative records such as Example Longevity Labs and Demo Health Market. Postgres mode is explicit and requires Postgres to be enabled. If Postgres is configured but unavailable, the API returns service unavailable rather than silently fabricating demo prices.

## Data-flow boundaries

The `market_intelligence` schema is separate from public scientific evidence tables, workflow data, human review state, and private profile data. Commercial relationships do not influence evidence scoring.

## Deletion and history behavior

Providers and offerings use restrictive relationships where appropriate. Historical price and availability observations cascade only when an offering is intentionally deleted through an administrative process. Observations are append-only for normal ingestion.

## Configuration

`MarketIntelligence:Provider` selects `Demo` or `Postgres`. `Postgres:Enabled` must be true for `Postgres` mode.

## Current limitations

The system does not yet normalize currencies, calculate cost effectiveness, rank offers, ingest real vendors, scrape websites, process purchases, or evaluate whether prices imply safety or efficacy.

## Future relationships

Evidence Explorer can later link an asset to its public offerings without mixing commercial data into evidence claims. Private profiles and budget-aware stacks can later consume public market data without storing personal health data in this public schema. Commerce and affiliate revenue may be evaluated later, but commercial relationships must remain separate from evidence scoring and are not implemented here.

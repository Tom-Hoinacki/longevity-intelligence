# Scientific source normalization

`ScientificSourceNormalizer` is a deterministic, provider-independent application boundary. It does not fetch sources, make medical conclusions, score evidence, log content, or persist database records.

Version `scientific-source-v1` applies Unicode NFC, line-ending and BOM normalization, non-breaking-space conversion, removal of disallowed control characters, trailing horizontal-whitespace removal, and blank-line trimming/collapse. It preserves case, punctuation, units, terminology, citations, tabs, and paragraph content.

Identity priority is DOI, PMID, ClinicalTrials.gov identifier, then canonical HTTP(S) URL. Identities are prefixed with `doi:`, `pmid:`, `clinicaltrials:`, or `url:`. URLs lose fragments and default ports but retain meaningful paths and queries.

The lowercase SHA-256 hash is computed over UTF-8 fields containing the version, normalized source type, title, identity, canonical URL, and normalized text, separated by LF characters. `ISourceNormalizer` adapts this deterministic result to workflow intake; Postgres then composes it with workflow and source-record identifiers. ClinicalTrials.gov identities are stored as `clinicaltrials:nct########` so they satisfy the migration identity constraint.

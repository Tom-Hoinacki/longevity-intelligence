# Scientific source normalization

`ScientificSourceNormalizer` is a deterministic, provider-independent application boundary. It does not fetch sources, make medical conclusions, score evidence, log content, or persist database records.

Version `scientific-source-v1` applies Unicode NFC, line-ending and BOM normalization, non-breaking-space conversion, removal of disallowed control characters, trailing horizontal-whitespace removal, and blank-line trimming/collapse. It preserves case, punctuation, units, terminology, citations, tabs, and paragraph content. Input is bounded to a 500-character title, 1,000,000-character body, and 2,048-character canonical URL. Accepted source types are `journal_article`, `preprint`, `clinical_trial`, `systematic_review`, and `meta_analysis`.

Identity priority is DOI, PMID, ClinicalTrials.gov identifier, then canonical HTTP(S) URL. Identities are prefixed with `doi:`, `pmid:`, `clinicaltrials:`, or `url:`. URLs lose fragments and default ports but retain meaningful paths and queries. ClinicalTrials.gov identities are stored as `clinicaltrials:nct########` so they satisfy the migration identity constraint.

The lowercase SHA-256 hash is computed over UTF-8 fields containing the version, normalized source type, title, identity, canonical URL, and normalized text, separated by LF characters. `ISourceNormalizer` adapts this result to workflow intake; PostgreSQL composes it with workflow and source-record identities. The original normalized text remains in the private `workflow` schema and is not copied to the public evidence graph.

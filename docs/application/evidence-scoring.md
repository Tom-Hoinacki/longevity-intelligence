# Deterministic evidence scoring

## Purpose and non-goals

The evidence-scoring subsystem provides the application-layer `Score -> Verdict` step. It converts a validated, structured assessment into a repeatable 0–100 ranking, an evidence verdict, and an audit-friendly explanation. It can also combine scored evidence items into a bounded claim-level result.

This is an application ranking policy. It is **not a medically validated evidence-grading framework**, does not establish clinical truth, and must not be used as medical advice. It does not infer assessment inputs, contact models or services, read a clock or environment, use randomness, persist data, or register itself with dependency injection. Human and domain review remain separate concerns.

## Data model

`EvidenceAssessment` is immutable and contains:

- a trimmed, non-empty evidence identity;
- study design, from mechanistic through meta-analysis;
- non-negative sample size and replication count;
- directness to the claim;
- risk of bias;
- consistency with other evidence;
- publication status;
- retraction and serious-methodological-limitation flags;
- optional conflict-of-interest severity; and
- claim alignment (`Supports`, `Contradicts`, or `Neutral`) for aggregation.

Categorical fields are enums and undefined enum values are rejected. Counts are checked both for non-negativity by the assessment and against policy maxima by the engine.

`EvidenceScoreResult` contains the evidence and policy identities, final score, verdict, alignment, dimension contributions, applied penalties, machine-readable reasons, and a concise explanation. Returned collections are read-only copies. Contributions, penalties, reasons, batch results, and aggregate evidence identities all have defined ordering.

## Default scoring policy

The immutable default policy identity is `evidence-scoring-v1`. Dimension weights total exactly 100:

| Dimension | Weight |
| --- | ---: |
| Study design | 20 |
| Sample size | 15 |
| Replication | 15 |
| Directness | 15 |
| Risk of bias | 15 |
| Consistency | 15 |
| Publication status | 5 |

Each category maps to a factor between zero and one:

| Dimension | Ordered factors |
| --- | --- |
| Study design | Mechanistic .15; animal .25; case report .30; cross-sectional .45; observational .60; randomized controlled trial .85; systematic review .90; meta-analysis 1.00 |
| Sample size | 0 = 0; 1–9 = .10; 10–49 = .30; 50–199 = .55; 200–999 = .75; 1,000+ = 1.00 |
| Replication count | 0 = 0; 1 = .35; 2 = .60; 3–4 = .80; 5+ = 1.00 |
| Directness | Indirect .25; partially direct .65; direct 1.00 |
| Risk of bias | Critical 0; high .25; moderate .65; low 1.00 |
| Consistency | Strongly contradictory 0; mixed .35; unknown .50; consistent .80; highly consistent 1.00 |
| Publication | Unpublished .40; preprint .75; peer reviewed 1.00 |

These mappings are deliberately explicit and monotonic. They express product ranking preferences, not universal scientific effect sizes.

## Score and penalty calculation

For each dimension:

```text
contribution = dimension weight * category factor
base score   = sum of contributions
final score  = max(0, base score - sum of explicit penalties)
```

Contributions are calculated with `decimal` and rounded to four decimal places, midpoint away from zero. The final score is rounded to two decimal places with the same rule. Because valid weights total 100, factors are bounded at one, and penalties are non-negative, the result is always between 0 and 100. The lower bound is part of the score formula; invalid input is never silently clamped.

Default explicit penalties are:

| Condition | Points |
| --- | ---: |
| Serious methodological limitations | 20 |
| Preprint | 3 |
| Unpublished | 7 |
| Conflict severity low / moderate / high | 1 / 4 / 8 |
| Retraction | 100 |

Publication status affects both its small weighted dimension and an explicit penalty. This intentionally distinguishes peer-review status while keeping the effect visible in the audit result. A missing conflict value and explicit `None` both apply no penalty.

## Verdicts and overrides

Default numeric thresholds are:

| Score | Numeric verdict |
| --- | --- |
| 75–100 | Strong |
| 50–74.99 | Moderate |
| 25–49.99 | Limited |
| 0–24.99 | Insufficient |

Overrides are applied after the numeric score:

1. A retracted item is always `Disqualified`, even if its pre-penalty quality is high.
2. A non-retracted item marked `StronglyContradictory` is always `Contradictory`, even when other dimensions leave a high numeric score.
3. An item with sample size below 10 and no replication cannot be more favorable than `Limited`.

The score remains visible when an override applies so consumers can distinguish measured input quality from the policy reason that controls the verdict. Reason codes identify every override.

## Batch evaluation

`EvaluateBatch` materializes the entire input, rejects null entries and duplicate evidence identities using ordinal comparison, and evaluates every item with the same method used for individual evaluation. An invalid item fails the batch; it is never skipped. Empty input returns an empty read-only list. Results are ordered by evidence identity with ordinal string ordering, independent of input or collection insertion order.

## Claim-level aggregation

Claim aggregation accepts distinct `EvidenceScoreResult` instances and orders them by evidence identity. `Disqualified` items are counted and reported but excluded from usable strength. Among usable items, alignment determines supporting, contradicting, and neutral groups.

The default bounded formula is:

```text
average quality = average score of all usable items
support share   = supporting score sum / (supporting score sum + contradicting score sum)
breadth factor  = min(1, directional item count / 3)
aggregate score = average quality * support share * breadth factor
```

If there is no directional supporting or contradicting strength, support share is zero. A directional item is aligned `Supports` or `Contradicts`; neutral items remain in average quality but cannot inflate directional breadth. Evidence identities must be distinct, so the breadth factor represents replication across separate items. One directional item receives one-third breadth and two receive two-thirds; three or more receive full breadth. Moderate requires at least two directional items and Strong requires at least three. All results must carry the same policy identity as the aggregator.

When contradicting score strength exceeds supporting strength, the aggregate verdict is `Contradictory` regardless of its numeric threshold. This prevents one excellent supporting item from overpowering numerous strong contradictions. Equal mixed strength is not called contradictory, but its support share and breadth reduce the score. Disqualifications do not automatically disqualify an entire claim; they remain explicit counts and reasons.

Empty or all-disqualified input produces score 0 and `Insufficient`. Aggregate results include the score, verdict, supporting/contradicting/disqualified counts, ordered reasons, explanation, and sorted included evidence identities.

## Determinism and validation

The subsystem has no model, database, network, clock, random, locale-sensitive ordering, or environment dependency. It uses decimal constants, explicit midpoint rounding, ordinal identity comparison, fixed dimension and penalty sequences, and sorted batch/aggregate identities. Identical logical inputs and policy produce identical values and ordering.

Policy construction rejects:

- empty policy identity;
- weights outside 0–100 or a total other than exactly 100;
- thresholds outside 0–100, equal thresholds, gaps implied by unordered boundaries, or overlaps;
- penalties outside 0–100;
- decreasing conflict penalties, an unpublished penalty below preprint, or an insufficient retraction penalty;
- non-positive input maxima, an impossible sparse boundary, or contradictory aggregate minimum counts.

Assessment construction rejects empty evidence identity, negative counts, and undefined enum values. Evaluation rejects counts above policy maxima. Failures are exceptions; no invalid configuration or data is normalized, ignored, or silently clamped.

## Worked examples

Under the default policy:

- A direct, peer-reviewed meta-analysis with sample size 1,000, five replications, low bias, and highly consistent evidence scores `100.00` and is `Strong`.
- The same otherwise favorable randomized controlled trial scores `97.00`. Marking it a preprint changes the publication contribution from 5 to 3.75 and applies a 3-point penalty, producing `92.75`.
- A favorable randomized controlled trial with sample size 9 and no replication scores `68.50`; the sparse-evidence rule caps its otherwise Moderate numeric verdict at `Limited`.
- A favorable randomized controlled trial marked strongly contradictory scores `82.00` but receives `Contradictory`.
- A retracted favorable randomized controlled trial receives the 100-point penalty, scores `0.00`, and is `Disqualified`.
- Three perfect supporting results aggregate to `100.00` and `Strong`. One perfect supporting result aggregates to `33.33` and `Limited` because of breadth. One perfect supporting result plus two perfect contradicting results also scores `33.33`, but contradiction dominance makes the aggregate `Contradictory`.

## Limitations and extension points

The policy uses coarse categorical judgments, does not model effect size, confidence intervals, population differences, endpoint importance, study dependence, publication bias, or domain-specific causal inference. Sample size tiers do not account for statistical power or design. Publication and conflict signals are ranking inputs rather than proof of quality or invalidity. Claim alignment must be supplied by a separate validated process.

Future persistence or API layers may serialize assessments, policies, and results and may inject a versioned policy explicitly. Such integrations should preserve decimal values, enum validation, reason codes, and ordering. They are intentionally not implemented here; persistence, API contracts, dependency-injection registration, and assessment generation belong in separate changes.

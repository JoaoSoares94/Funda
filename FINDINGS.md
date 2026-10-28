# Full Amsterdam search audit

## Scope and method

- Collected every page for `type=koop&zo=/amsterdam/&pagesize=25` on 28 October 2026.
- This is the search without the garden filter. All 226 pages were collected fresh into `.local/amsterdam-audit/`.
- The collector spaced request starts by at least one second. Two HTTP 504 responses recovered after retries: 228 requests in total, with no HTTP 429 responses observed.
- Each successful response is saved unchanged as `all-page-N.json`. Credentials were checked before caching and are not included in the artifacts.
- All pages reported 226 pages and 5,631 top-level results, and every current-page value matched its requested page.
- Replayed all responses through the current `FundaClient` twice, with agent-name fallback disabled and enabled. HTTP was replaced with a cache handler and time with `FakeTimeProvider`; these replays made no network requests.
- Ranked replayed listings using the current `AgentRankingService`, including its ordinal name and numeric ID tie breakers.

## Property accounting

| Observation | Count |
|---|---:|
| Top-level entries | 5,631 |
| Parents containing children | 155 |
| Child entries | 278 |
| Unique property IDs | 5,909 |
| Unique GlobalId values | 5,909 |
| Duplicate property IDs | 0 |
| IDs appearing as both parent and child | 0 |
| Parents missing MakelaarId | 64 |
| Children missing MakelaarId | 2 |
| Properties with conflicting supplied broker IDs | 0 |
| Entries marked sold | 0 |
| Entries not searchable | 0 |
| Entries marked IndProjectObjectType | 0 |
| Entries with missing addresses | 0 |

The API's reported total of 5,631 matches the top-level results. Expanding all returned children exposes another 278 distinct property records. The maximum nesting depth in this dataset is one child level.

Every child has a different property ID, GlobalId, and address from its parent. Every child shares the parent's GroupByObjectType. Where both broker IDs are supplied, they agree. None of the returned records is marked as a project object, and all 5,909 have SoortAanbod=10. For grouped parents, the separate Project.GlobalId metadata never equals the parent's own GlobalId. These observations support counting the parent and each child as separate returned properties, rather than treating children as duplicate representations of the parent.

## Resolution results

| Agent-name fallback | Ranked | Unresolved | Inferred broker identities |
|---|---:|---:|---:|
| Disabled | 5,847 | 62 | 4 |
| Enabled | 5,902 | 7 | 59 |

There are 5,843 properties with a directly supplied broker ID. Child consensus recovers four parents. Agent-name fallback recovers a further 55 properties: 53 parents and both children with missing broker IDs.

Both replays verified that:

- Ranked plus unresolved properties equals the complete set of 5,909 unique IDs.
- No property is emitted more than once.
- Every nonconflicting supplied broker ID is preserved and its property is included.
- Final completion summaries are produced after the full enumeration.

### Parents resolved from children

Positions below are one-based positions in each page's Objects array.

| Page | Position | Parent address | Resolved broker ID |
|---|---:|---|---:|
| 2 | 17 | Postjesweg 248 | 24067 |
| 4 | 22 | Postjesweg 27-B | 24705 |
| 139 | 8 | Plantage Middenlaan 142 | 24607 |
| 145 | 11 | Elandsgracht 178 | 24705 |

### Children without MakelaarId

The complete dataset disproves any general assumption that children always supply a broker ID.

| Page | Parent position | Child address | Child ID | Name fallback broker ID |
|---|---:|---|---|---:|
| 135 | 21 | Vondelstraat 195-G | 9fac7427-1659-4e6a-996c-05ec8ebd787f | 24705 |
| 140 | 15 | Albert Cuypstraat 162 | 8e872ea7-18ff-411e-ab0d-7c16499953d0 | 24587 |

The first is named Eefje Voogd Makelaardij, the second Van de Steege Projectmakelaars. Both names match one known broker ID elsewhere in this search. They are excluded when name fallback is disabled; they are not assigned an ID merely because their parent has one.

### Ambiguous broker names

Broker names are not globally unique within the response set. The current case-insensitive, trimmed-name lookup correctly refuses these ambiguous names:

| Broker name | Supplied broker IDs |
|---|---|
| Peters en Partners Makelaars en Taxateurs | 24809, 24638, 24857 |
| Ameo makelaars | 24469, 24625, 24785 |
| Verhoef Vastgoed | 55118, 80895 |

### Seven properties still unresolved with name fallback

| Page | Position | Address | Broker name | Reason |
|---|---:|---|---|---|
| 14 | 3 | Van Woustraat 229 | Ameo makelaars | Name belongs to three IDs |
| 24 | 10 | Westerstraat 118 | VLIEG Makelaars Hoofddorp OG | No known exact normalized name match |
| 42 | 11 | Pieter Calandlaan 87-F | VOORMA EN WALCH MAKELAARS IN HET GOOI | No known exact normalized name match |
| 66 | 12 | Zuiderzeeweg 243 | Staats Huysen | No known exact normalized name match |
| 71 | 22 | Insulindeweg 212 | Van Groenigen Makelaardij B.V. | No known exact normalized name match |
| 78 | 7 | Utrechtsestraat 6 | Bataglia Consulting Home | No known exact normalized name match |
| 203 | 23 | Burgemeester de Vlugtlaan 185 | Mr. Broker | No known exact normalized name match |

All seven are parents with no children and no direct broker ID. Assigning an ID would require additional evidence or a guessing rule. They remain explicitly accounted for as unresolved.

## Top ten including children

Both columns use child consensus; only optional agent-name fallback differs. Membership and order of the top ten are unchanged between these two modes in this snapshot.

| Rank | Broker ID | Broker | Name fallback off | Name fallback on |
|---|---:|---|---:|---:|
| 1 | 24648 | Heeren Makelaars | 201 | 203 |
| 2 | 24705 | Eefje Voogd Makelaardij | 194 | 196 |
| 3 | 24605 | Hallie & Van Klooster Makelaardij | 172 | 173 |
| 4 | 24067 | Broersma Wonen | 156 | 157 |
| 5 | 24592 | Ramón Mossel Makelaardij o.g. B.V. | 129 | 130 |
| 6 | 24599 | DSTRCT \| Forbes Global Properties | 104 | 104 |
| 7 | 24607 | KRK Makelaars Amsterdam | 79 | 80 |
| 8 | 24079 | Makelaardij Van der Linden Amsterdam | 76 | 77 |
| 9 | 24789 | VON POLL REAL ESTATE | 71 | 72 |
| 10 | 24614 | Nieuw West Makelaardij B.V. | 70 | 71 |

## Effect of counting children

The 276 children with direct broker IDs contribute to 12 brokers when name fallback is disabled:

| Broker | Ranked children | Total ranked properties |
|---|---:|---:|
| Eefje Voogd Makelaardij | 75 | 194 |
| Hallie & Van Klooster Makelaardij | 54 | 172 |
| Van de Steege Projectmakelaars | 36 | 61 |
| Nieuw West Makelaardij B.V. | 22 | 70 |
| Broersma Wonen | 18 | 156 |
| Ramón Mossel Makelaardij o.g. B.V. | 15 | 129 |
| Lunshof Makelaardij Amsterdam | 15 | 39 |
| KRK Makelaars Amsterdam | 11 | 79 |
| Brockhoff Nieuwbouw | 10 | 24 |
| Bert van Vulpen makelaars + hypotheken Amsterdam | 8 | 31 |
| FRIS Woningmakelaars Amsterdam | 8 | 44 |
| Sense Vastgoed & Makelaardij | 4 | 6 |

Totals also include parents resolved from children where applicable. For example, Eefje Voogd has 117 directly identified parents, 75 directly identified children, and two parents resolved through children: 194 ranked properties without name fallback. With name fallback, it gains two more properties for a total of 196.

Considering only parents with direct broker IDs, Eefje Voogd ranks fourth and Broersma second. Including children and child consensus moves Eefje Voogd to second and Broersma to fourth. Nieuw West enters the top ten; Openbare Makelaardij leaves it.

## Interpretation and limits

The current algorithm accounts for all 5,909 properties returned in this complete search. Missing broker identities are explicit, child records are not silently dropped, and broker-name collisions do not produce guessed assignments. The audit found no defect requiring an application change.

These are observations from one collected response set, not guarantees of the API's contract. Collection took several minutes and the endpoint does not provide a snapshot token; stable pagination totals alone cannot establish an atomic snapshot. Deduplication cannot recover properties omitted by the upstream API or unseen changes between pages. Name-based recovery remains an inference, reported separately from directly supplied IDs.
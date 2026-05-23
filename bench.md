# Product Search Benchmark Results

Comparison of five approaches to searching ~1M product rows in PostgreSQL from .NET 10 / EF Core, measured with BenchmarkDotNet.

**Environment:**
- BenchmarkDotNet v0.15.8 on Linux Mint 22.2
- AMD Ryzen 5 PRO 4650U, 12 logical / 6 physical cores
- .NET SDK 10.0.101, .NET 10.0.1 RyuJIT
- PostgreSQL in Docker (`localhost:5433`), 1,000,000 products

---

## Full Results Table

| Method    | Query           | Mean         | Error       | StdDev      | Ratio  | RatioSD | Rows | Gen0    | Gen1   | Allocated | Alloc Ratio |
|---------- |---------------- |-------------:|------------:|------------:|-------:|--------:|-----:|--------:|-------:|----------:|------------:|
| **Naive**     | **ergonomic chair** | **180,957.8 μs** | **2,882.68 μs** | **3,431.62 μs** |  **1.000** |    **0.03** |    **-** |       **-** |      **-** |  **13.29 KB** |        **1.00** |
| Smart     | ergonomic chair |   2,499.9 μs |    49.87 μs |   126.93 μs |  0.014 |    0.00 |   50 | 15.6250 |      - |  35.78 KB |        2.69 |
| Fts       | ergonomic chair | 266,021.0 μs | 3,336.23 μs | 2,785.90 μs |  1.471 |    0.03 |   50 |       - |      - |  32.27 KB |        2.43 |
| FtsFast   | ergonomic chair |     918.5 μs |    25.29 μs |    73.36 μs |  0.005 |    0.00 |   50 | 11.7188 |      - |  26.38 KB |        1.98 |
| FtsRanked | ergonomic chair |  31,400.9 μs |   519.56 μs |   486.00 μs |  0.174 |    0.00 |   50 | 62.5000 |      - | 149.27 KB |       11.23 |
|           |                 |              |             |             |        |         |      |         |        |           |             |
| **Naive**     | **mouse**           |     **998.0 μs** |    **25.58 μs** |    **74.62 μs** |   **1.01** |    **0.11** |   **50** | **11.7188** |      **-** |  **26.59 KB** |        **1.00** |
| Smart     | mouse           |   1,701.6 μs |    33.82 μs |    72.79 μs |   1.71 |    0.15 |   50 | 11.7188 |      - |  27.66 KB |        1.04 |
| Fts       | mouse           | 170,583.2 μs | 3,219.38 μs | 3,161.86 μs | 171.87 |   13.18 |   50 |       - |      - |  32.11 KB |        1.21 |
| FtsFast   | mouse           |     853.6 μs |    23.62 μs |    69.26 μs |   0.86 |    0.09 |   50 | 11.7188 |      - |  26.35 KB |        0.99 |
| FtsRanked | mouse           |   3,197.2 μs |    63.81 μs |    91.52 μs |   3.22 |    0.26 |   50 | 54.6875 | 7.8125 | 144.76 KB |        5.44 |

### Headline numbers

| Query | Naive | Smart | Fts (ranked) | FtsFast (unranked) | FtsRanked (CTE) |
|---|---:|---:|---:|---:|---:|
| `ergonomic chair` | 181 ms | 2.5 ms | **266 ms** | **0.92 ms** | **31.4 ms** |
| `mouse` | 1.0 ms | 1.7 ms | **170 ms** | **0.85 ms** | **3.2 ms** |

- **FtsFast wins on raw speed** in both cases — sub-ms.
- **FtsRanked is the best ranked option without extensions** — 8–53× faster than Fts.
- **Fts (`ORDER BY ts_rank`) is the worst** because it has to score every match before LIMIT.
- **Naive is unpredictable** — fast when the substring is common, full-scan-slow when rare.

---

## The Five Approaches

### 1. Naive — `LIKE '%query%'`

```csharp
db.Products
    .Where(p => EF.Functions.Like(p.Name, $"%{query}%")
             || EF.Functions.Like(p.Description, $"%{query}%"))
    .Take(50)
    .ToListAsync();
```

Generated SQL:
```sql
SELECT p.id, p.brand, p.category, p.created_at, p.description, p.name, p.price, p.search_vector
FROM products AS p
WHERE p.name LIKE @query_contains OR p.description LIKE @query_contains
LIMIT @p
```

Case-sensitive substring match on the *exact* query string. No index can serve a leading-wildcard `LIKE`, so Postgres falls back to a sequential scan.

- **`mouse`** (1.0 ms): scan ends almost immediately — 50 hits found after reading 737 rows.
- **`ergonomic chair`** (181 ms): the *exact* substring never appears in the data (products contain "Ergonomic Granite Chair", "Ergonomic Soft Chair", etc., never the contiguous "ergonomic chair"). Postgres scans the entire 1M-row table in parallel and returns 0 rows.

**Takeaway:** performance is entirely dependent on result density and exact substring match. Sparse/no matches = full table scan.

---

### 2. Smart — multi-token `ILIKE` with AND

```csharp
// Split query into tokens; each token must appear in name OR description
var tokens = query.Split(' ');
var queryable = db.Products.AsQueryable();
foreach (var token in tokens)
{
    var pattern = $"%{token}%";
    queryable = queryable.Where(p =>
        EF.Functions.ILike(p.Name, pattern) ||
        EF.Functions.ILike(p.Description, pattern));
}
return await queryable.Take(50).ToListAsync();
```

Generated SQL (for `ergonomic chair`):
```sql
SELECT p.id, p.brand, p.category, p.created_at, p.description, p.name, p.price, p.search_vector
FROM products AS p
WHERE (p.name ILIKE @pattern ESCAPE '' OR p.description ILIKE @pattern ESCAPE '')
  AND (p.name ILIKE @pattern5 ESCAPE '' OR p.description ILIKE @pattern5 ESCAPE '')
LIMIT @p
```

Splits the query into tokens and requires each token to appear (case-insensitive) somewhere in name or description. Still a sequential scan, but matches are *much* more plentiful.

- **`ergonomic chair`** (2.5 ms): finds 50 matches after filtering only ~580 rows — the AND of two common tokens yields plenty of hits early.
- **`mouse`** (1.7 ms): single-token degenerates to one `ILIKE`; slightly slower than Naive because `ILIKE` is more expensive than case-sensitive `LIKE`.

**Takeaway:** dramatically better recall than Naive for multi-word queries because results no longer require an exact contiguous substring.

---

### 3. Fts — Full-Text Search with `ts_rank` ordering

```csharp
db.Products
    .Where(p => p.SearchVector.Matches(EF.Functions.PlainToTsQuery("english", query)))
    .OrderByDescending(p => p.SearchVector.Rank(EF.Functions.PlainToTsQuery("english", query)))
    .Take(50)
    .ToListAsync();
```

Generated SQL:
```sql
SELECT p.id, p.brand, p.category, p.created_at, p.description, p.name, p.price, p.search_vector
FROM products AS p
WHERE p.search_vector @@ plainto_tsquery('english', @query)
ORDER BY ts_rank(p.search_vector, plainto_tsquery('english', @query)) DESC
LIMIT @p
```

Uses the GIN index on `search_vector` to find matches, then orders by relevance score.

**Why it's slow** (170–266 ms) *despite* the index:
- The `@@` filter is matched cheaply via Bitmap Index Scan (~22 ms).
- But `ORDER BY ts_rank(...)` forces Postgres to compute the rank for **every matching row** (88,948 for "ergonomic chair"; 121,317 for "mouse") before sorting and applying LIMIT.
- The plan shows `Bitmap Heap Scan` reading 46k+ pages just to score them all → 250+ ms on heap reads.
- Sort step (top-N heapsort) is cheap; the cost is the full heap fetch needed to compute rank.

**Takeaway:** `ts_rank` in `ORDER BY` defeats the LIMIT optimization — you pay for every match, not just the top 50.

---

### 4. FtsFast — FTS without ranking

```csharp
db.Products
    .Where(p => p.SearchVector.Matches(EF.Functions.PlainToTsQuery("english", query)))
    .Take(50)
    .ToListAsync();
```

Generated SQL:
```sql
SELECT p.id, p.brand, p.category, p.created_at, p.description, p.name, p.price, p.search_vector
FROM products AS p
WHERE p.search_vector @@ plainto_tsquery('english', @query)
LIMIT @p
```

Same filter as `Fts`, but **no `ORDER BY ts_rank`**.

**Why it's fast** (<1 ms):
- Postgres can stop reading as soon as 50 rows pass the filter.
- The plan switches to a `Seq Scan` (planner chose this over the index because LIMIT 50 with high match density is reached in a few pages).
- For "ergonomic chair": 50 hits found after 536 rows scanned, 33 buffer pages, 0.3 ms.
- For "mouse": 50 hits found after 442 rows scanned, 0.2 ms.

**Takeaway:** removing relevance ranking turns FTS into a near-instant top-K filter. If you don't *need* ranked results, never include `ts_rank` in the `ORDER BY`.

---

### 5. FtsRanked — Two-stage CTE (fast filtering + ranking on a candidate pool) ⭐ NEW

```csharp
const string sql = """
    WITH candidates AS MATERIALIZED (
        SELECT p.id, p.brand, p.category, p.created_at, p.description,
               p.name, p.price, p.search_vector
        FROM products AS p
        WHERE p.search_vector @@ plainto_tsquery('english', {0})
        LIMIT {1}                                       -- candidate pool (500)
    )
    SELECT id, brand, category, created_at, description, name, price, search_vector
    FROM candidates
    ORDER BY ts_rank(search_vector, plainto_tsquery('english', {0})) DESC
    LIMIT {2}                                            -- result limit (50)
    """;

return await db.Products
    .FromSqlRaw(sql, query, CandidatePoolSize, ResultLimit)
    .AsNoTracking()
    .ToListAsync();
```

**The trick:** separate filtering from ranking with a `MATERIALIZED` CTE. Postgres pulls a capped 500-row candidate pool through the GIN/Seq scan (which can stop as soon as 500 hits are found), then ranks only that subset.

**Results:**
- **`mouse`** (3.2 ms): the planner picks a Seq Scan since matches are plentiful; finds 500 candidates after scanning ~4,000 rows in 1.3 ms, then top-N heapsorts them in <1 ms.
- **`ergonomic chair`** (31.4 ms): the planner still picks Bitmap Index Scan because matches are sparser; the bitmap is built fully (~20 ms on the index) before the heap scan can short-circuit at 500 rows. Hence the ~30 ms — most of it is the bitmap build, not heap I/O.

**Trade-off:** ranking is **approximate** — the top-50 of 500 candidates is not guaranteed to equal the true top-50 of (say) 121k matches. For ecommerce/autocomplete this is usually invisible. Tune `CandidatePoolSize` to balance recall vs latency.

**Allocation:** higher (5–11× baseline) because `FromSqlRaw` + `AsNoTracking` still materializes the candidate set's metadata. The two-stage SQL also returns slightly wider tuples through the CTE projection.

**Takeaway:** the best ranked FTS option *without* installing PostgreSQL extensions. 8–53× faster than naive `ORDER BY ts_rank`.

---

## EXPLAIN ANALYZE Plans

### Naive — `ergonomic chair` (0 rows, 175 ms)

```
Limit  (cost=1000.00..33213.14 rows=50 width=421) (actual time=168.218..175.761 rows=0 loops=1)
  Buffers: shared hit=16040 read=41484
  ->  Gather  (cost=1000.00..64782.01 rows=99 width=421) (actual time=168.216..175.758 rows=0 loops=1)
        Workers Planned: 2
        Workers Launched: 2
        ->  Parallel Seq Scan on public.products p  (cost=0.00..63772.11 rows=41 width=421) (actual time=162.431..162.432 rows=0 loops=3)
              Filter: ((p.name ~~ '%ergonomic chair%'::text) OR (p.description ~~ '%ergonomic chair%'::text))
              Rows Removed by Filter: 333333
Planning Time: 0.222 ms
Execution Time: 175.791 ms
```
Full table scan, 3 parallel workers, removes all 1M rows because nothing matches the exact substring.

### Naive — `mouse` (50 rows, 0.6 ms)

```
Limit  (cost=0.00..44.01 rows=50 width=421) (actual time=0.040..0.494 rows=50 loops=1)
  Buffers: shared hit=16 read=31
  ->  Seq Scan on public.products p  (cost=0.00..72519.47 rows=82399 width=421) (actual time=0.039..0.488 rows=50 loops=1)
        Filter: ((p.name ~~ '%mouse%'::text) OR (p.description ~~ '%mouse%'::text))
        Rows Removed by Filter: 737
Planning Time: 0.148 ms
Execution Time: 0.556 ms
```
Sequential scan terminates after ~800 rows because hits are dense.

### Smart — `ergonomic chair` (50 rows, 3.9 ms)

```
Limit  (cost=0.00..162.89 rows=50 width=421) (actual time=0.040..3.900 rows=50 loops=1)
  ->  Seq Scan on public.products p  (cost=0.00..77517.96 rows=23794 width=421) (actual time=0.038..3.889 rows=50 loops=1)
        Filter: (((p.name ~~* '%ergonomic%'::text) OR (p.description ~~* '%ergonomic%'::text))
            AND ((p.name ~~* '%chair%'::text) OR (p.description ~~* '%chair%'::text)))
        Rows Removed by Filter: 581
Planning Time: 0.733 ms
Execution Time: 3.978 ms
```
Tokenized AND of `%ergonomic%` and `%chair%` returns many hits — short-circuits at 50 after ~630 rows.

### Smart — `mouse` (50 rows, 0.7 ms)

```
Limit  (cost=0.00..36.02 rows=50 width=421) (actual time=0.013..0.674 rows=50 loops=1)
  ->  Seq Scan on public.products p  (cost=0.00..72519.47 rows=100667 width=421) (actual time=0.012..0.669 rows=50 loops=1)
        Filter: ((p.name ~~* '%mouse%'::text) OR (p.description ~~* '%mouse%'::text))
        Rows Removed by Filter: 289
Planning Time: 0.201 ms
Execution Time: 0.717 ms
```

### Fts — `ergonomic chair` (50 rows, 277 ms)

```
Limit  (cost=47952.78..47952.91 rows=50 width=425) (actual time=276.898..276.907 rows=50 loops=1)
  Buffers: shared hit=49 read=46190
  ->  Sort  (cost=47952.78..48018.12 rows=26137 width=425) (actual time=276.896..276.899 rows=50 loops=1)
        Sort Key: (ts_rank(p.search_vector, '''ergonom'' & ''chair'''::tsquery)) DESC
        Sort Method: top-N heapsort  Memory: 52kB
        ->  Bitmap Heap Scan on public.products p  (cost=377.30..47084.53 rows=26137 width=425) (actual time=29.569..249.489 rows=88948 loops=1)
              Recheck Cond: (p.search_vector @@ '''ergonom'' & ''chair'''::tsquery)
              Heap Blocks: exact=46096
              ->  Bitmap Index Scan on ix_products_search_vector  (cost=0.00..370.77 rows=26137 width=0) (actual time=22.188..22.188 rows=88948 loops=1)
                    Index Cond: (p.search_vector @@ '''ergonom'' & ''chair'''::tsquery)
Planning Time: 0.261 ms
Execution Time: 277.504 ms
```
**Key observation:** Bitmap Index Scan finishes in 22 ms (cheap!), but the Bitmap Heap Scan reads **46,096 pages** to score 88,948 matches — 250 ms just on heap fetch. The LIMIT 50 can only apply after sorting all 88,948 scored rows.

### Fts — `mouse` (50 rows, 180 ms)

```
Limit  (cost=61774.42..61780.25 rows=50 width=425) (actual time=170.077..179.683 rows=50 loops=1)
  Buffers: shared hit=10 read=51498
  ->  Gather Merge  (cost=61774.42..73500.00 rows=100498 width=425) (actual time=170.075..179.677 rows=50 loops=1)
        Workers Planned: 2
        Workers Launched: 2
        ->  Sort  (cost=60774.39..60900.02 rows=50249 width=425) (actual time=164.539..164.541 rows=50 loops=3)
              Sort Key: (ts_rank(p.search_vector, '''mous'''::tsquery)) DESC
              Sort Method: top-N heapsort  Memory: 59kB
              ->  Parallel Bitmap Heap Scan on public.products p  (cost=827.43..59105.16 rows=50249 width=425) (actual time=23.104..151.635 rows=40439 loops=3)
                    Recheck Cond: (p.search_vector @@ '''mous'''::tsquery)
                    Heap Blocks: exact=19236
                    ->  Bitmap Index Scan on ix_products_search_vector  (cost=0.00..797.28 rows=120597 width=0) (actual time=20.944..20.944 rows=121317 loops=1)
                          Index Cond: (p.search_vector @@ '''mous'''::tsquery)
Planning Time: 0.213 ms
Execution Time: 179.814 ms
```
Same story, parallelized: 121k matches all get scored before LIMIT.

### FtsFast — `ergonomic chair` (50 rows, 0.3 ms)

```
Limit  (cost=0.00..133.95 rows=50 width=421) (actual time=0.015..0.316 rows=50 loops=1)
  Buffers: shared hit=30 read=3
  ->  Seq Scan on public.products p  (cost=0.00..70020.23 rows=26137 width=421) (actual time=0.014..0.308 rows=50 loops=1)
        Filter: (p.search_vector @@ '''ergonom'' & ''chair'''::tsquery)
        Rows Removed by Filter: 486
Planning Time: 0.210 ms
Execution Time: 0.335 ms
```
**The planner abandons the index entirely** — with no `ORDER BY`, it estimates that scanning sequentially is cheaper than building a bitmap, since matches are dense enough that 50 hits arrive after ~540 rows.

### FtsFast — `mouse` (50 rows, 0.2 ms)

```
Limit  (cost=0.00..29.03 rows=50 width=421) (actual time=0.009..0.186 rows=50 loops=1)
  Buffers: shared hit=24 read=3
  ->  Seq Scan on public.products p  (cost=0.00..70020.23 rows=120597 width=421) (actual time=0.009..0.182 rows=50 loops=1)
        Filter: (p.search_vector @@ '''mous'''::tsquery)
        Rows Removed by Filter: 392
Planning Time: 0.158 ms
Execution Time: 0.198 ms
```

### FtsRanked — `ergonomic chair` (50 rows, 30 ms)

```
Limit  (cost=1297.42..1297.55 rows=50 width=196) (actual time=28.987..28.994 rows=50 loops=1)
  Buffers: shared hit=417
  CTE candidates
    ->  Limit  (cost=377.30..1269.56 rows=500 width=421) (actual time=27.647..28.087 rows=500 loops=1)
          Buffers: shared hit=417
          ->  Bitmap Heap Scan on public.products p  (cost=377.30..47019.19 rows=26137 width=421) (actual time=27.646..28.053 rows=500 loops=1)
                Recheck Cond: (p.search_vector @@ '''ergonom'' & ''chair'''::tsquery)
                Heap Blocks: exact=274
                ->  Bitmap Index Scan on ix_products_search_vector  (cost=0.00..370.77 rows=26137 width=0) (actual time=20.241..20.241 rows=88948 loops=1)
                      Index Cond: (p.search_vector @@ '''ergonom'' & ''chair'''::tsquery)
  ->  Sort  (cost=27.86..29.11 rows=500 width=196) (actual time=28.986..28.988 rows=50 loops=1)
        Sort Key: (ts_rank(candidates.search_vector, '''ergonom'' & ''chair'''::tsquery)) DESC
        Sort Method: top-N heapsort  Memory: 55kB
        ->  CTE Scan on candidates  (cost=0.00..11.25 rows=500 width=196) (actual time=27.667..28.715 rows=500 loops=1)
Planning Time: 0.254 ms
Execution Time: 29.535 ms
```
**Key observations:**
- The CTE's inner `Limit 500` short-circuits the **Bitmap Heap Scan** at 500 rows (only 274 pages read vs 46,096 for `Fts`).
- But the **Bitmap Index Scan still builds the full bitmap** (88,948 entries, ~20 ms) before the heap scan can take advantage of LIMIT — this is the dominant cost.
- Final sort scans 500 CTE rows, ranks them, returns top 50 in <1 ms.
- ~9× faster than `Fts` (30 ms vs 277 ms) despite still building the full bitmap.

### FtsRanked — `mouse` (50 rows, 2.4 ms)

```
Limit  (cost=318.17..318.29 rows=50 width=196) (actual time=2.267..2.272 rows=50 loops=1)
  Buffers: shared hit=7 read=223
  CTE candidates
    ->  Limit  (cost=0.00..290.31 rows=500 width=421) (actual time=0.032..1.311 rows=500 loops=1)
          Buffers: shared hit=7 read=223
          ->  Seq Scan on public.products p  (cost=0.00..70020.23 rows=120597 width=421) (actual time=0.031..1.270 rows=500 loops=1)
                Filter: (p.search_vector @@ '''mous'''::tsquery)
                Rows Removed by Filter: 3507
  ->  Sort  (cost=27.86..29.11 rows=500 width=196) (actual time=2.265..2.267 rows=50 loops=1)
        Sort Key: (ts_rank(candidates.search_vector, '''mous'''::tsquery)) DESC
        Sort Method: top-N heapsort  Memory: 65kB
        ->  CTE Scan on candidates  (cost=0.00..11.25 rows=500 width=196) (actual time=0.041..1.937 rows=500 loops=1)
Planning Time: 0.364 ms
Execution Time: 2.443 ms
```
**Even better path:** the planner picks a **Seq Scan** for the CTE (no bitmap build), finds 500 candidates after scanning ~4,000 rows in 1.3 ms, then sorts in memory. **71× faster than `Fts`** (2.4 ms vs 180 ms).

---

## Key Insights from the Plans

1. **`ORDER BY ts_rank` is a trap.** It forces a full materialization of all matches. The cost differential between Fts (266 ms) and FtsFast (0.9 ms) on "ergonomic chair" is **~290×**.

2. **The two-stage CTE recovers most of the lost speed.** FtsRanked is 8–53× faster than Fts and *still* returns rank-ordered results — at the cost of being approximate (top-N of a candidate pool, not the true top-N).

3. **For sparse-but-popular tokens, the bitmap *index* scan is the new bottleneck.** Even with `LIMIT 500` inside the CTE, Postgres builds the full bitmap for "ergonomic chair" before any heap rows are read — that's where the 30 ms goes. To eliminate this, you'd need an index that returns results pre-sorted (RUM extension) or a Seq Scan with a lower selectivity threshold.

4. **Naive is unpredictable.** On "mouse" it's 1 ms; on "ergonomic chair" it's 181 ms. Performance depends on whether the *exact substring* exists. For a real product search, this variance is unacceptable.

5. **Smart is a reasonable no-index fallback.** It's the only LIKE-based approach that beats Naive on the rare-substring case (2.5 ms vs 181 ms). But it's still a sequential scan — won't scale past a few hundred thousand rows if matches become sparser.

6. **FtsFast wins overall** if you can live without exact relevance ranking. Sub-millisecond on both queries, consistent low memory allocation, returns 50 matches in physical row order (essentially arbitrary).

7. **Allocation matters too.** FtsRanked allocates **5–11× more** than the unranked variants because the CTE returns wider tuples and `FromSqlRaw` re-materializes through EF. If allocation matters more than ranking, prefer FtsFast.

---

## Recommendations

| Use case | Recommended approach |
|---|---|
| Autocomplete, faceted filters, existence checks | **FtsFast** (sub-ms, no ranking) |
| Ranked product search, "good enough" relevance | **FtsRanked** (2–30 ms, approximate top-N) |
| Exact relevance scoring required, scale ≤100k rows | `Fts` (slow but accurate, OK at small scale) |
| Exact relevance scoring at 1M+ rows | Install `rum` extension for native fast-ordered FTS |
| Fuzzy / typo-tolerant search | `pg_trgm` with `<->` similarity operator |
| Never use | **Naive `LIKE '%...%'`** in production — data-dependent worst-case is a full scan |

---

## Other fast-ranked options (not implemented here)

| Approach | Speed | Notes |
|----------|-------|-------|
| **RUM index** (`CREATE EXTENSION rum`) | ★★★★★ | True fast-ordered FTS via `<=>` operator. The index stores positional info so ranking can be done during the index scan, eliminating the bitmap-build cost. Requires extension install. |
| **Trigram similarity** (`pg_trgm` + `<->`) | ★★★★ | `ORDER BY name <-> @query` is index-friendly with GIN trigram index. Different semantics — fuzzy match instead of stemmed FTS. Good for typo tolerance. |
| **Weighted tsvector + name-first cascade** | ★★★ | `setweight('A')` on name, `setweight('B')` on description. Run a strict query against name-only first; only fall back to description if recall is too low. More complex application logic. |

---

## Methodology Notes

- All benchmarks include EF Core query translation overhead, not just raw SQL execution.
- The Postgres connection is opened in `[GlobalSetup]` so connection-acquisition time doesn't pollute measurements.
- `SqlPlanDiagnoser` re-runs each query with `EXPLAIN (ANALYZE, BUFFERS, VERBOSE)` after the benchmark loop, so the plans shown above are real EXPLAIN output, not estimates.
- The Smart benchmark on "ergonomic chair" shows a **bimodal distribution** (mValue ≈ 3.0), likely caused by buffer cache warm/cold transitions. Doesn't change the conclusion.
- Outliers were removed conservatively (1–4 per method) and don't affect the order-of-magnitude differences.
- `Naive` on "ergonomic chair" returns **0 rows** — the comparison is technically against an empty result set, but the work done (full scan) is real and the benchmark is honest about it.

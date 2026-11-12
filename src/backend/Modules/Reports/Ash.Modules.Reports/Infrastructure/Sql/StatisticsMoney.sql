-- Dëmi parandaluar / shkaktuar of the levels closed in the year: per court AND currency — never summed across
-- currencies. The rollup adds one total row per currency (GroupedCourt = 1); its grand total over all currencies
-- (GroupedCurrency = 1) is dropped by the API. GROUPING() tells a rollup row from a real NULL (a decision with only m²
-- has no currency). m² is reported in the same rows (its own columns, never mixed with money).
SELECT f.court_id                                       AS CourtId,
       f.currency                                       AS Currency,
       GROUPING(f.currency)                             AS GroupedCurrency,
       GROUPING(f.court_id)                             AS GroupedCourt,
       SUM(IFNULL(f.prevented_amount, 0))               AS PreventedAmount,
       SUM(IFNULL(f.prevented_m2, 0))                   AS PreventedM2,
       SUM(IFNULL(f.caused_amount, 0))                  AS CausedAmount,
       SUM(IFNULL(f.caused_m2, 0))                      AS CausedM2
  FROM f
 WHERE f.m_demi = 1
   AND (f.prevented_amount IS NOT NULL OR f.prevented_m2 IS NOT NULL OR f.caused_amount IS NOT NULL OR f.caused_m2 IS NOT NULL)
 GROUP BY f.currency, f.court_id WITH ROLLUP

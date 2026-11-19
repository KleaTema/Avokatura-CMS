-- Task 053o — "Rezultati i çështjeve të përfunduara" INSIDE Tabela 3 / 4 (the client's columns): per court and result
-- column (statistics_result_mapping, f.result_column), followed by ASH / delegated or related. The rollup adds each
-- column's total over the courts (GroupedCourt = 1); its grand total over all columns (GroupedColumn = 1) is dropped.
SELECT f.result_column                  AS ColumnCode,
       f.court_id                       AS CourtId,
       GROUPING(f.result_column)        AS GroupedColumn,
       GROUPING(f.court_id)             AS GroupedCourt,
       SUM(f.m_perfunduar_ash)          AS Ash,
       SUM(f.m_perfunduar_deleguar)     AS Deleguar
  FROM f
 WHERE (f.m_perfunduar_ash = 1 OR f.m_perfunduar_deleguar = 1) AND f.result_column IS NOT NULL
 GROUP BY f.result_column, f.court_id WITH ROLLUP

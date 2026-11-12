-- Rezultati i çështjeve të përfunduara gjatë vitit, per decision result: followed by ASH / delegated or related.
SELECT dr.code                           AS ResultCode,
       dr.name                           AS ResultName,
       SUM(f.m_perfunduar_ash)           AS Ash,
       SUM(f.m_perfunduar_deleguar)      AS Deleguar
  FROM f JOIN decision_results dr ON dr.id = f.result_id
 WHERE f.m_perfunduar_ash = 1 OR f.m_perfunduar_deleguar = 1
 GROUP BY dr.id, dr.code, dr.name, dr.sort_order
 ORDER BY dr.sort_order, dr.id

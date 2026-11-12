-- "Në dorë": open cases followed by ASH at the end point, per primary lawyer at that point.
SELECT f.primary_at_end AS LawyerId, SUM(f.m_ne_dore) AS Count
  FROM f
 WHERE f.m_ne_dore = 1
 GROUP BY f.primary_at_end
 ORDER BY Count DESC, f.primary_at_end

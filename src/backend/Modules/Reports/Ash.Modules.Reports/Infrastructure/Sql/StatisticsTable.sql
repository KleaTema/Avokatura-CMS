-- Tabela 5 / 6: every count measure per court of the level, and the TOTAL row (court_id NULL, WITH ROLLUP).
SELECT f.court_id                          AS CourtId,
       MAX(ct.name)                        AS CourtName,
       SUM(f.m_mbartur_aktive)             AS mbartur_aktive,
       SUM(f.m_ardhur_gjate_vitit)         AS ardhur_gjate_vitit,
       SUM(f.m_total)                      AS total,
       SUM(f.m_aktive)                     AS aktive,
       SUM(f.m_deleguar)                   AS deleguar,
       SUM(f.m_relatuar)                   AS relatuar,
       SUM(f.m_aktive_ndjekje)             AS aktive_ndjekje,
       SUM(f.m_perfunduar_ash)             AS perfunduar_ash,
       SUM(f.m_perfunduar_deleguar)        AS perfunduar_deleguar,
       SUM(f.m_transferuar)                AS transferuar,
       SUM(f.m_pezulluar_ash)              AS pezulluar_ash,
       SUM(f.m_pezulluar_deleguar)         AS pezulluar_deleguar,
       SUM(f.m_ankimuar_rekursuar)         AS ankimuar_rekursuar,
       SUM(f.m_mbartur_pritje_vendimi)     AS mbartur_pritje_vendimi,
       SUM(f.m_mbartur_ardhur_vendim)      AS mbartur_ardhur_vendim
  FROM f JOIN courts ct ON ct.id = f.court_id
 GROUP BY f.court_id WITH ROLLUP

-- Drill-down: the cases behind ONE number — the rows of f whose flag for @measure is 1, narrowed like the cell
-- (court, currency, result, lawyer of the "Në dorë" row). Keyset on the case id. The measure is a bound parameter
-- matched in a CASE (never a column name built in code).
SELECT f.case_id AS CaseId
  FROM f
 WHERE (CASE @measure
          WHEN 'mbartur_aktive'          THEN f.m_mbartur_aktive
          WHEN 'ardhur_gjate_vitit'      THEN f.m_ardhur_gjate_vitit
          WHEN 'total'                   THEN f.m_total
          WHEN 'aktive'                  THEN f.m_aktive
          WHEN 'deleguar'                THEN f.m_deleguar
          WHEN 'relatuar'                THEN f.m_relatuar
          WHEN 'aktive_ndjekje'          THEN f.m_aktive_ndjekje
          WHEN 'perfunduar_ash'          THEN f.m_perfunduar_ash
          WHEN 'perfunduar_deleguar'     THEN f.m_perfunduar_deleguar
          WHEN 'transferuar'             THEN f.m_transferuar
          WHEN 'pezulluar_ash'           THEN f.m_pezulluar_ash
          WHEN 'pezulluar_deleguar'      THEN f.m_pezulluar_deleguar
          WHEN 'ankimuar_rekursuar'      THEN f.m_ankimuar_rekursuar
          WHEN 'ankimuar'                THEN f.m_ankimuar
          WHEN 'rekursuar'               THEN f.m_rekursuar
          WHEN 'mbartur_pritje_vendimi'  THEN f.m_mbartur_pritje_vendimi
          WHEN 'mbartur_ardhur_vendim'   THEN f.m_mbartur_ardhur_vendim
          WHEN 'ne_dore'                 THEN f.m_ne_dore
          WHEN 'demi'                    THEN f.m_demi AND (f.prevented_amount IS NOT NULL OR f.prevented_m2 IS NOT NULL
                                                            OR f.caused_amount IS NOT NULL OR f.caused_m2 IS NOT NULL)
          ELSE 0 END) = 1
   AND (@court_id IS NULL OR f.court_id = @court_id)
   AND (@currency IS NULL OR f.currency = @currency)
   AND (@result_id IS NULL OR f.result_id = @result_id)
   -- task 053o: a cell of the table's result columns (statistics_result_mapping)
   AND (@result_column IS NULL OR f.result_column = @result_column)
   AND (@row_lawyer_id IS NULL OR f.primary_at_end = @row_lawyer_id)
   AND f.case_id > @after_id
 ORDER BY f.case_id
 LIMIT @take

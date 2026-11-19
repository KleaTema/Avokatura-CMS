-- ---------------------------------------------------------------------------------------------------------------------
-- Task 044 — the MEASURE FLAGS (CTE "f"): one row per case, one 0/1 column m_* per measure of Tabela 5 / 6. Every number
-- the API shows is a SUM of one of these columns (or of an amount where m_demi = 1), and the drill-down lists the cases
-- whose flag is 1 — so a number and its cases can never disagree. Each measure is defined HERE ONLY.
-- Input "base": the live flags (StatisticsLive.sql) or, for a closed year with a snapshot, the frozen rows.
-- Parameters:
--   @use_snapshot      1 = read statistics_snapshot_cases of @year, 0 = live
--   @year              the year (snapshot key)
--   @office_id         NULL = every office
--   @lawyer_all        1 = every lawyer; 0 = only the lawyers of @lawyer_ids (task 053h: one lawyer, or an assistant's
--                      linked lawyers): open measures by the primary at the end point, closed measures by the primary
--                      at closing — a case has one primary at a time, so with several lawyers it is never counted twice
--   @lawyer_ids        the lawyers (a list, expanded by Dapper; empty = no case)
--   @scope_inst        NULL = no institution scope; else (task 053h, institution user / jurist) only the cases where
--                      this institution is a party or linked — in Tabela 5 too
--   @scope_inst_children 1 = @scope_inst is a ministry: its direct agencies count too
--   @f_level, @f_court  NULL = every court; else the court level (code) / the court of the level (task 053h)
--   @institution_id    NULL = Tabela 5; else Tabela 6: the institution is a non-deleted party or linked (active link)
--   @with_children     1 = the institution is a ministry: its direct agencies count too (parent_institution_id, task 015)
--   @scope_head        NULL = no Kryetar scope; else that Kryetar's cases: @scope_office, supervised, own assignments
--   @scope_office      the Kryetar's office (with @scope_head)
-- ---------------------------------------------------------------------------------------------------------------------
base AS (
  SELECT live.* FROM live WHERE @use_snapshot = 0
  UNION ALL
  SELECT s.case_id, s.office_id, s.court_id, s.primary_at_end, s.primary_at_close, s.is_delegim, s.is_relatim,
         s.is_suspended, s.result_id, s.state_position, s.currency, s.prevented_amount, s.prevented_m2, s.caused_amount,
         s.caused_m2, s.open_start, s.reg_in_year, s.open_end, s.closed_in_year, s.transferred, s.appealed, s.recursed,
         s.pending_at_start, s.decision_arrived
    FROM statistics_snapshot_cases s
   WHERE @use_snapshot = 1 AND s.year = @year
),
scoped AS (
  SELECT b.*,
         (@lawyer_all = 1 OR b.primary_at_end IN @lawyer_ids)   AS lawyer_open_ok,
         (@lawyer_all = 1 OR b.primary_at_close IN @lawyer_ids) AS lawyer_closed_ok,
         (b.is_delegim = 1 OR b.is_relatim = 1)                  AS is_deleg
    FROM base b
   WHERE (@office_id IS NULL OR b.office_id = @office_id)
     -- the builder's court level and court (task 053h): the court of each level, as the table's rows
     AND (@f_level IS NULL OR b.court_id IN (SELECT fc.id FROM courts fc JOIN court_levels fl ON fl.id = fc.court_level_id
                                             WHERE fl.code = @f_level))
     AND (@f_court IS NULL OR b.court_id = @f_court)
     -- the Kryetar's case scope (task 053d): own office, supervised (CaseScopeSpec.SupervisedSql, put in by StatisticsSql), own assignment
     AND (@scope_head IS NULL
          OR b.office_id = @scope_office
          OR /*SUPERVISED*/
          OR EXISTS (SELECT 1 FROM case_assignments sc WHERE sc.case_id = b.case_id AND sc.user_id = @scope_head AND sc.ended_at IS NULL
                      AND sc.case_role IN ('PRIMARY_LAWYER', 'SECONDARY_LAWYER', 'LEGAL_ASSISTANT')))
     -- the institution user's / jurist's scope (task 053h): ReportAudience.InstitutionSql, put in by StatisticsSql
     AND (@scope_inst IS NULL OR /*INSTITUTION_SCOPE*/)
     AND (@institution_id IS NULL
          OR EXISTS (SELECT 1 FROM case_parties p JOIN institutions i ON i.id = p.institution_id
                      WHERE p.case_id = b.case_id AND p.deleted_at IS NULL
                        AND (i.id = @institution_id OR (@with_children = 1 AND i.parent_institution_id = @institution_id)))
          OR EXISTS (SELECT 1 FROM case_institutions ci JOIN institutions i ON i.id = ci.institution_id
                      WHERE ci.case_id = b.case_id AND ci.ended_at IS NULL
                        AND (i.id = @institution_id OR (@with_children = 1 AND i.parent_institution_id = @institution_id))))
),
f AS (
  SELECT s.*,
         -- Mbartur aktive: open at the start of the year
         (s.open_start AND s.lawyer_open_ok)                                          AS m_mbartur_aktive,
         -- Ardhur gjatë vitit: registered during the year
         (s.reg_in_year AND s.lawyer_open_ok)                                         AS m_ardhur_gjate_vitit,
         -- Total: mbartur + ardhur (disjoint: one is registered before the year, the other in it)
         ((s.open_start OR s.reg_in_year) AND s.lawyer_open_ok)                       AS m_total,
         -- Aktive: open at the end of the year (today for the current year)
         (s.open_end AND s.lawyer_open_ok)                                            AS m_aktive,
         (s.open_end AND s.is_delegim AND s.lawyer_open_ok)                           AS m_deleguar,
         (s.open_end AND s.is_relatim AND s.lawyer_open_ok)                           AS m_relatuar,
         (s.open_end AND NOT s.is_deleg AND s.lawyer_open_ok)                         AS m_aktive_ndjekje,
         -- Përfunduar: closed during the year, followed by ASH / delegated or related
         (s.closed_in_year AND NOT s.is_deleg AND s.lawyer_closed_ok)                 AS m_perfunduar_ash,
         (s.closed_in_year AND s.is_deleg AND s.lawyer_closed_ok)                     AS m_perfunduar_deleguar,
         -- Transferuar: the primary lawyer was replaced during the year
         (s.transferred AND s.lawyer_open_ok)                                         AS m_transferuar,
         -- Pezulluar: open at the end and suspended
         (s.open_end AND s.is_suspended AND NOT s.is_deleg AND s.lawyer_open_ok)      AS m_pezulluar_ash,
         (s.open_end AND s.is_suspended AND s.is_deleg AND s.lawyer_open_ok)          AS m_pezulluar_deleguar,
         -- Ankimuar / rekursuar: closed during the year with an appeal or recourse (report_settings
         -- statistics.appeal_columns: COMBINED shows this one; SPLIT the two below — task 053o)
         (s.closed_in_year AND (s.appealed OR s.recursed) AND s.lawyer_closed_ok)     AS m_ankimuar_rekursuar,
         (s.closed_in_year AND s.appealed AND s.lawyer_closed_ok)                     AS m_ankimuar,
         (s.closed_in_year AND s.recursed AND s.lawyer_closed_ok)                     AS m_rekursuar,
         -- the result's column of "Rezultati i çështjeve të përfunduara" (task 053o): statistics_result_mapping —
         -- the row for the State's side first, then ANY; no row → TJETER. Data, not code: a client answer changes rows.
         IF(s.result_id IS NULL, NULL, COALESCE(
           (SELECT rm.column_code FROM statistics_result_mapping rm JOIN decision_results rd ON rd.code = rm.decision_result_code
             WHERE rd.id = s.result_id AND rm.state_position = s.state_position),
           (SELECT rm.column_code FROM statistics_result_mapping rm JOIN decision_results rd ON rd.code = rm.decision_result_code
             WHERE rd.id = s.result_id AND rm.state_position = 'ANY'),
           'TJETER'))                                                                 AS result_column,
         -- Mbartur në pritje të vendimit të arsyetuar: closed before the year, decision still awaited on 1 January
         (s.pending_at_start AND s.lawyer_closed_ok)                                  AS m_mbartur_pritje_vendimi,
         -- Mbartur, ardhur vendimi: closed before the year, reasoned decision received during it
         (s.decision_arrived AND s.lawyer_closed_ok)                                  AS m_mbartur_ardhur_vendim,
         -- Në dorë: open at the end, followed by ASH, with a primary lawyer (grouped by that lawyer)
         (s.open_end AND NOT s.is_deleg AND s.primary_at_end IS NOT NULL AND s.lawyer_open_ok) AS m_ne_dore,
         -- Dëmi (parandaluar / shkaktuar): the amounts of the cases closed during the year, per currency
         (s.closed_in_year AND s.lawyer_closed_ok)                                    AS m_demi
    FROM scoped s
)

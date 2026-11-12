-- ---------------------------------------------------------------------------------------------------------------------
-- Task 044 — the MEASURE FLAGS (CTE "f"): one row per case, one 0/1 column m_* per measure of Tabela 5 / 6. Every number
-- the API shows is a SUM of one of these columns (or of an amount where m_demi = 1), and the drill-down lists the cases
-- whose flag is 1 — so a number and its cases can never disagree. Each measure is defined HERE ONLY.
-- Input "base": the live flags (StatisticsLive.sql) or, for a closed year with a snapshot, the frozen rows.
-- Parameters:
--   @use_snapshot      1 = read statistics_snapshot_cases of @year, 0 = live
--   @year              the year (snapshot key)
--   @office_id         NULL = every office
--   @lawyer_id         NULL = every lawyer; else open measures by the primary at the end point, closed measures by
--                      the primary at closing
--   @institution_id    NULL = Tabela 5; else Tabela 6: the institution is a non-deleted party or linked (active link)
--   @with_children     1 = the institution is a ministry: its direct agencies count too (parent_institution_id, task 015)
-- ---------------------------------------------------------------------------------------------------------------------
base AS (
  SELECT live.* FROM live WHERE @use_snapshot = 0
  UNION ALL
  SELECT s.case_id, s.office_id, s.court_id, s.primary_at_end, s.primary_at_close, s.is_delegim, s.is_relatim,
         s.is_suspended, s.result_id, s.currency, s.prevented_amount, s.prevented_m2, s.caused_amount, s.caused_m2,
         s.open_start, s.reg_in_year, s.open_end, s.closed_in_year, s.transferred, s.appealed, s.pending_at_start,
         s.decision_arrived
    FROM statistics_snapshot_cases s
   WHERE @use_snapshot = 1 AND s.year = @year
),
scoped AS (
  SELECT b.*,
         (@lawyer_id IS NULL OR b.primary_at_end = @lawyer_id)   AS lawyer_open_ok,
         (@lawyer_id IS NULL OR b.primary_at_close = @lawyer_id) AS lawyer_closed_ok,
         (b.is_delegim = 1 OR b.is_relatim = 1)                  AS is_deleg
    FROM base b
   WHERE (@office_id IS NULL OR b.office_id = @office_id)
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
         -- Ankimuar / rekursuar: closed during the year with an appeal or recourse
         (s.closed_in_year AND s.appealed AND s.lawyer_closed_ok)                     AS m_ankimuar_rekursuar,
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

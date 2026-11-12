-- ---------------------------------------------------------------------------------------------------------------------
-- Task 044 — the per-case BASE FLAGS of one year, computed from the live tables (CTE "live"). Each definition of
-- docs/statistics-definitions.md is written here ONCE as a 0/1 flag; every measure is a SUM of flags (StatisticsFlags.sql).
-- The yearly snapshot stores exactly these columns (statistics_snapshot_cases), so a closed year reads the same shape.
-- Parameters (all bound, never concatenated):
--   @y_start  the year's first instant: 1 January 00:00 Europe/Tirane, as UTC (computed in C#, not MAKEDATE in UTC —
--             a case registered on 31.12. at 23:30 in Tirane is 22:30 UTC and belongs to THAT year)
--   @y_end    the next 1 January 00:00 Tirane, as UTC
--   @point    the "end" of the year for open-at-end measures: @y_end, or NOW for the current year
-- Fixes against docs/sql/example_statistics.sql:
--   * point in time, not "now": "open" means open AT the start / end of the year (registered before, not closed by
--     then), so a case closed in 2027 still counts as open at the end of 2026 — the reference used the status of today;
--   * the court is the court of each LEVEL (c.court_id): an appeal /2 counts at the appeal court;
--   * money stays per currency (currency is a column here; sums are grouped by it), m² separate;
--   * "mbartur në pritje" = closed before the year AND the reasoned decision not received by its first day.
-- ---------------------------------------------------------------------------------------------------------------------
live AS (
  SELECT c.id        AS case_id,
         c.office_id AS office_id,
         c.court_id  AS court_id,
         -- the primary lawyer AT the end point (assigned before it, not ended by then)
         (SELECT a.user_id FROM case_assignments a
           WHERE a.case_id = c.id AND a.case_role = 'PRIMARY_LAWYER'
             AND a.assigned_at < @point AND (a.ended_at IS NULL OR a.ended_at >= @point)
           ORDER BY a.assigned_at DESC LIMIT 1)                                                   AS primary_at_end,
         -- the primary lawyer when the level was closed
         (SELECT a.user_id FROM case_assignments a
           WHERE a.case_id = c.id AND a.case_role = 'PRIMARY_LAWYER' AND c.closed_at IS NOT NULL
             AND a.assigned_at <= c.closed_at AND (a.ended_at IS NULL OR a.ended_at > c.closed_at)
           ORDER BY a.assigned_at DESC LIMIT 1)                                                   AS primary_at_close,
         -- no history: the CURRENT category and suspension (a closed year reads them frozen from its snapshot)
         IFNULL(cat.code = 'DELEGIM', 0)                                                          AS is_delegim,
         IFNULL(cat.code = 'RELATIM', 0)                                                          AS is_relatim,
         c.is_suspended                                                                           AS is_suspended,
         d.result_id                                                                              AS result_id,
         d.currency                                                                               AS currency,
         d.prevented_damage_amount                                                                AS prevented_amount,
         d.prevented_damage_area_m2                                                               AS prevented_m2,
         d.caused_damage_amount                                                                   AS caused_amount,
         d.caused_damage_area_m2                                                                  AS caused_m2,
         -- open at the start of the year: registered before it, not closed before it
         (c.registered_at < @y_start AND (c.closed_at IS NULL OR c.closed_at >= @y_start))         AS open_start,
         -- registered during the year
         (c.registered_at >= @y_start AND c.registered_at < @y_end)                                AS reg_in_year,
         -- open at the end of the year (or today for the current year)
         (c.registered_at < @point AND (c.closed_at IS NULL OR c.closed_at >= @point))             AS open_end,
         -- closed during the year
         IFNULL(c.closed_at >= @y_start AND c.closed_at < @y_end, 0)                               AS closed_in_year,
         -- the primary lawyer was transferred during the year
         EXISTS (SELECT 1 FROM case_assignments t
                  WHERE t.case_id = c.id AND t.case_role = 'PRIMARY_LAWYER' AND t.end_reason = 'TRANSFERRED'
                    AND t.ended_at >= @y_start AND t.ended_at < @y_end)                            AS transferred,
         -- the decision was appealed (ankim) or recursed (rekurs)
         IFNULL(d.is_appealed = 1 OR d.is_recursed = 1, 0)                                         AS appealed,
         -- closed before the year, the reasoned decision still awaited on its first day
         IFNULL(c.closed_at < @y_start
                AND (d.decision_received = 0 OR d.decision_received_at >= @y_start), 0)            AS pending_at_start,
         -- closed before the year, the reasoned decision arrived during it
         IFNULL(c.closed_at < @y_start
                AND d.decision_received_at >= @y_start AND d.decision_received_at < @y_end, 0)    AS decision_arrived
    FROM cases c
    LEFT JOIN case_categories cat ON cat.id = c.category_id
    LEFT JOIN case_decisions d    ON d.case_id = c.id
   -- only cases that can count in the year: registered before its end, and open in it or closed in it or awaiting
   -- (or receiving) their reasoned decision during it
   WHERE c.registered_at < @y_end
     AND (c.closed_at IS NULL OR c.closed_at >= @y_start
          OR d.decision_received = 0 OR d.decision_received_at >= @y_start)
)

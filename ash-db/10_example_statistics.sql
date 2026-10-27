-- =====================================================================
-- ASH — 10: statistics queries (Tabela 5 "Statistika e çështjeve aktive")
-- Reference implementation of the memo's definitions. Run on the READ
-- REPLICA as ash_reporting. Parameters: @year, @office_id (NULL = all).
-- The API wraps these in parameterised queries (never string-concatenated).
-- =====================================================================
USE ash_core;

SET @year      = COALESCE(@year, YEAR(UTC_DATE()));
SET @y_start   = MAKEDATE(@year, 1);                 -- 1 janar
SET @y_end     = MAKEDATE(@year + 1, 1);             -- 1 janar i vitit pasardhës
-- @office_id stays NULL unless set by the caller (= all offices)

-- ---------------------------------------------------------------------
-- 1) Per court (gjykata of the current level of each case)
-- ---------------------------------------------------------------------
-- Step 1 (CTE "f"): one row per case with NULL-safe 0/1 flags — each
-- definition from the memo is written exactly once, in plain SQL.
-- Step 2: sum the flags per court.
WITH f AS (
  SELECT c.id, ct.name AS court_name,
         (c.lifecycle_status <> 'CLOSED')                                     AS is_open,
         (c.registered_at < @y_start)                                         AS reg_before_year,
         (c.registered_at >= @y_start AND c.registered_at < @y_end)           AS reg_in_year,
         IFNULL(c.closed_at >= @y_start AND c.closed_at < @y_end, 0)          AS closed_in_year,
         IFNULL(c.closed_at < @y_start, 0)                                    AS closed_before_year,
         IFNULL(cat.is_delegation, 0)                                         AS is_deleg,
         IFNULL(cat.code = 'DELEGIM', 0)                                      AS is_delegim,
         IFNULL(cat.code = 'RELATIM', 0)                                      AS is_relatim,
         c.is_suspended,
         (tr.case_id IS NOT NULL)                                             AS transferred_in_year,
         IFNULL(d.is_appealed = 1 OR d.is_recursed = 1, 0)                    AS appealed,
         IFNULL(d.decision_received = 0, 0)                                   AS decision_pending,
         IFNULL(d.decision_received_at >= @y_start AND d.decision_received_at < @y_end, 0) AS decision_arrived_in_year,
         IFNULL(d.prevented_damage_amount, 0)  AS prev_amt, IFNULL(d.prevented_damage_area_m2, 0) AS prev_m2,
         IFNULL(d.caused_damage_amount, 0)     AS caus_amt, IFNULL(d.caused_damage_area_m2, 0)    AS caus_m2
    FROM cases c
    JOIN courts ct                ON ct.id = c.court_id
    LEFT JOIN case_categories cat ON cat.id = c.category_id
    LEFT JOIN case_decisions d    ON d.case_id = c.id
    -- "Transferuar si çështje": primary lawyer changed during the year
    LEFT JOIN (SELECT DISTINCT a.case_id
                 FROM case_assignments a
                WHERE a.case_role = 'PRIMARY_LAWYER' AND a.end_reason = 'TRANSFERRED'
                  AND a.ended_at >= @y_start AND a.ended_at < @y_end) tr ON tr.case_id = c.id
   WHERE (@office_id IS NULL OR c.office_id = @office_id)
)
SELECT COALESCE(court_name, 'TOTAL')                              AS gjykata,
  SUM(is_open AND reg_before_year)                                AS mbartur_aktive,
  SUM(is_open AND reg_in_year)                                    AS ardhur_gjate_vitit,
  SUM(is_open)                                                    AS total,
  SUM(is_open AND is_delegim)                                     AS deleguar,
  SUM(is_open AND is_relatim)                                     AS relatuar,
  SUM(is_open AND NOT is_deleg)                                   AS aktive_ndjekje,
  SUM(closed_in_year AND NOT is_deleg)                            AS perfunduar_ndjekur_ash,
  SUM(closed_in_year AND is_deleg)                                AS perfunduar_deleguar_relatuar,
  SUM(transferred_in_year)                                        AS transferuar,
  SUM(is_open AND is_suspended AND NOT is_deleg)                  AS pezulluar_ash,
  SUM(is_open AND is_suspended AND is_deleg)                      AS pezulluar_deleguar,
  SUM(IF(closed_in_year, prev_amt, 0))                            AS demi_parandaluar_leke,
  SUM(IF(closed_in_year, prev_m2,  0))                            AS demi_parandaluar_m2,
  SUM(IF(closed_in_year, caus_amt, 0))                            AS demi_shkaktuar_leke,
  SUM(IF(closed_in_year, caus_m2,  0))                            AS demi_shkaktuar_m2,
  SUM(closed_in_year AND appealed)                                AS ankimuar_rekursuar,
  SUM(closed_before_year AND decision_pending)                    AS mbartur_pritje_vendimi_arsyetuar,
  SUM(closed_before_year AND decision_arrived_in_year)            AS mbartur_ardhur_vendim
FROM f
GROUP BY court_name WITH ROLLUP;

-- ---------------------------------------------------------------------
-- 2) Rezultati i çështjeve të përfunduara, split ASH vs deleguar/relatuar
-- ---------------------------------------------------------------------
SELECT dr.name AS rezultati,
       SUM(IFNULL(cat.is_delegation, 0) = 0) AS ndjekur_nga_ash,
       SUM(IFNULL(cat.is_delegation, 0) = 1) AS deleguar_relatuar
  FROM cases c
  JOIN case_decisions d    ON d.case_id = c.id
  JOIN decision_results dr ON dr.id = d.result_id
  LEFT JOIN case_categories cat ON cat.id = c.category_id
 WHERE c.closed_at >= @y_start AND c.closed_at < @y_end
   AND (@office_id IS NULL OR c.office_id = @office_id)
 GROUP BY dr.sort_order, dr.name
 ORDER BY dr.sort_order;

-- ---------------------------------------------------------------------
-- 3) "Në dorë" per primary lawyer (open cases in ndjekje)
-- ---------------------------------------------------------------------
SELECT CONCAT(u.first_name, ' ', u.last_name) AS avokati, COUNT(*) AS ne_dore
  FROM case_assignments a
  JOIN cases c ON c.id = a.case_id
  JOIN users u ON u.id = a.user_id
  LEFT JOIN case_categories cat ON cat.id = c.category_id
 WHERE a.case_role = 'PRIMARY_LAWYER' AND a.ended_at IS NULL
   AND c.lifecycle_status <> 'CLOSED'
   AND COALESCE(cat.is_delegation, 0) = 0
   AND (@office_id IS NULL OR c.office_id = @office_id)
 GROUP BY u.id, u.first_name, u.last_name
 ORDER BY ne_dore DESC;

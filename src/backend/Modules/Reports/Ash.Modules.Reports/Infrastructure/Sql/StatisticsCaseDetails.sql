-- The drill-down's columns for a page of case ids: code, court of the level, the primary lawyer (the current one, or
-- the last one of a closed level), status, registration and closing time.
SELECT c.public_id        AS PublicId,
       c.case_code        AS CaseCode,
       ct.name            AS Court,
       (SELECT CONCAT(u.first_name, ' ', u.last_name)
          FROM case_assignments a JOIN users u ON u.id = a.user_id
         WHERE a.case_id = c.id AND a.case_role = 'PRIMARY_LAWYER'
         ORDER BY a.ended_at IS NULL DESC, a.assigned_at DESC
         LIMIT 1)         AS Lawyer,
       c.lifecycle_status AS Status,
       c.registered_at    AS RegisteredAt,
       c.closed_at        AS ClosedAt
  FROM cases c
  JOIN courts ct ON ct.id = c.court_id
 WHERE c.id IN @ids
 ORDER BY c.id

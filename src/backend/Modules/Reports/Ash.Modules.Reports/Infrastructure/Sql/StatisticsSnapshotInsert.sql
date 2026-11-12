-- The yearly snapshot: the live base flags of @year (end point = the year's end) frozen per case.
INSERT INTO statistics_snapshot_cases
  (year, case_id, office_id, court_id, primary_at_end, primary_at_close, is_delegim, is_relatim, is_suspended,
   result_id, currency, prevented_amount, prevented_m2, caused_amount, caused_m2, open_start, reg_in_year, open_end,
   closed_in_year, transferred, appealed, pending_at_start, decision_arrived)
SELECT @year, live.case_id, live.office_id, live.court_id, live.primary_at_end, live.primary_at_close, live.is_delegim,
       live.is_relatim, live.is_suspended, live.result_id, live.currency, live.prevented_amount, live.prevented_m2,
       live.caused_amount, live.caused_m2, live.open_start, live.reg_in_year, live.open_end, live.closed_in_year,
       live.transferred, live.appealed, live.pending_at_start, live.decision_arrived
  FROM live

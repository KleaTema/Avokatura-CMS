USE ash_core;

-- Substring search ("640" finds 2026-686640/1, "stri" finds "Ministria"): FULLTEXT indexes WITH PARSER ngram
-- (ngram_token_size = 2, the server default). Queries use MATCH … AGAINST in BOOLEAN MODE; accents are folded by the
-- columns' utf8mb4_0900_ai_ci collation ("shendetesise" finds "Shëndetësisë").
--
-- Stopwords OFF: with them on, MySQL drops every ngram token that is an English stopword ("in", "an", "on", "at",
-- "is", "it", "of", "or", "to" …), which breaks Albanian substrings (M-in-istria, Fin-an-cave, Tir-an-ë). The
-- setting is read when an index is built, so it is set here; the server config (docs/sql/mysql_server.cnf,
-- docker-compose) sets it too, so a later rebuild (OPTIMIZE, ALTER … FORCE) keeps the tokens.
SET SESSION innodb_ft_enable_stopword = OFF;

-- InnoDB builds one FULLTEXT index per ALTER TABLE.
--
-- Two indexes with the SAME columns would make MATCH use whichever was created first (the old word index), so the
-- word-parser indexes on cases(object_text) and judges(first_name, last_name) are replaced; ngram tokens also find
-- whole words. Every other existing index is kept.

-- cases: the case code, act number and summons number ("640", "86640", part of an act number)
ALTER TABLE cases ADD FULLTEXT KEY ftn_cases_numbers (case_code, act_number, summons_number) WITH PARSER ngram;

-- cases: the object of the dispute
ALTER TABLE cases DROP INDEX ft_cases_object;
ALTER TABLE cases ADD FULLTEXT KEY ftn_cases_object (object_text) WITH PARSER ngram;

-- case_parties: name and NIPT (ft_cp_name on display_name alone stays)
ALTER TABLE case_parties ADD FULLTEXT KEY ftn_cp_name_identifier (display_name, identifier) WITH PARSER ngram;

-- judges: admin list search
ALTER TABLE judges DROP INDEX ft_judges_name;
ALTER TABLE judges ADD FULLTEXT KEY ftn_judges_name (first_name, last_name) WITH PARSER ngram;

-- institutions: admin list and picker search (ft_institutions_name on name, short_name stays)
ALTER TABLE institutions ADD FULLTEXT KEY ftn_institutions_search (name, short_name, nipt) WITH PARSER ngram;

-- users: admin list search (ft_users_name on first_name, last_name stays)
ALTER TABLE users ADD FULLTEXT KEY ftn_users_search (first_name, last_name, email) WITH PARSER ngram;

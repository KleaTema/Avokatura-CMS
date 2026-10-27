-- =====================================================================
-- LOCAL DEVELOPMENT ONLY — never run on the AKSHI servers.
-- Courts and judges are empty in the real database (the official lists are
-- imported later). This seeds a few of them for local work and tests.
-- =====================================================================
USE ash_core;

INSERT INTO courts (name, court_level_id, city)
SELECT v.name, l.id, v.city
FROM (
  SELECT 'Gjykata e Shkallës së Parë Tiranë' AS name, 'FIRST_INSTANCE' AS level_code, 'Tiranë' AS city
  UNION ALL SELECT 'Gjykata e Shkallës së Parë Durrës',   'FIRST_INSTANCE', 'Durrës'
  UNION ALL SELECT 'Gjykata e Apelit Tiranë',             'APPEAL',         'Tiranë'
  UNION ALL SELECT 'Gjykata e Apelit Shkodër',            'APPEAL',         'Shkodër'
  UNION ALL SELECT 'Gjykata e Lartë',                     'HIGH_COURT',     'Tiranë'
) v
JOIN court_levels l ON l.code = v.level_code;

INSERT INTO judges (first_name, last_name, court_id)
SELECT v.first_name, v.last_name, c.id
FROM (
  SELECT 'Ilir' AS first_name, 'Gjyqtari' AS last_name, 'Gjykata e Shkallës së Parë Tiranë' AS court_name
  UNION ALL SELECT 'Klodiana', 'Çela',     'Gjykata e Shkallës së Parë Tiranë'
  UNION ALL SELECT 'Arbër',    'Dëshira',  'Gjykata e Shkallës së Parë Tiranë'
  UNION ALL SELECT 'Besnik',   'Hoxha',    'Gjykata e Shkallës së Parë Durrës'
  UNION ALL SELECT 'Elda',     'Kërçuku',  'Gjykata e Shkallës së Parë Durrës'
  UNION ALL SELECT 'Genc',     'Muçaj',    'Gjykata e Apelit Tiranë'
  UNION ALL SELECT 'Vjollca',  'Ndreu',    'Gjykata e Apelit Tiranë'
  UNION ALL SELECT 'Dritan',   'Gjokëni',  'Gjykata e Apelit Shkodër'
  UNION ALL SELECT 'Mirela',   'Çaushi',   'Gjykata e Apelit Shkodër'
  UNION ALL SELECT 'Fatmir',   'Hysëni',   'Gjykata e Lartë'
  UNION ALL SELECT 'Liljana',  'Shehu',    'Gjykata e Lartë'
  UNION ALL SELECT 'Përparim', 'Ëmbla',    'Gjykata e Lartë'
) v
JOIN courts c ON c.name = v.court_name;

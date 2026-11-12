# Statistikat — përkufizimet (Tabela 5 dhe Tabela 6)

**Për miratim nga klienti.** Ky dokument shpjegon me fjalë të thjeshta si llogaritet çdo shifër e statistikave
vjetore. Çdo përkufizim është shkruar një herë të vetme në sistem (`StatisticsLive.sql` dhe `StatisticsFlags.sql`) dhe
çdo shifër është numri i çështjeve që plotësojnë përkufizimin — prandaj, duke klikuar një shifër, shihen pikërisht ato
çështje.

## Rregulla të përgjithshme

- **Viti** është viti kalendarik sipas orës së Tiranës: nga 1 janari ora 00:00 deri më 31 dhjetor ora 24:00.
  *Shembull:* një çështje e regjistruar më 31.12.2026 në orën 23:30 (ora e Tiranës) llogaritet në vitin 2026, edhe pse
  sipas orës UTC ishte ende 22:30.
- **Në një moment të caktuar, jo "sot".** "E hapur në fund të vitit" do të thotë: e regjistruar para fundit të vitit dhe
  e pambyllur deri atëherë. *Shembull:* një çështje e hapur gjatë 2026 dhe e mbyllur në mars 2027 llogaritet si aktive
  në fund të 2026 — edhe kur raporti i 2026 shihet sot. Për vitin në vazhdim, "fundi i vitit" është momenti i tanishëm.
- **Çdo shkallë është një çështje më vete** (X/1, X/2, …) dhe **gjykata është gjykata e asaj shkalle**: një apel X/2
  numërohet te gjykata e apelit, jo te gjykata e shkallës së parë.
- **Paratë sipas monedhës.** Shumat (dëmi i parandaluar / i shkaktuar) jepen veçmas për çdo monedhë (ALL, EUR, …) dhe
  nuk mblidhen kurrë mes monedhave. Sipërfaqja (m²) jepet në kolonë më vete.
- **Kategoria (Ndjekje / Delegim / Relatim) dhe pezullimi** nuk kanë histori në sistem: për vitin në vazhdim përdoret
  vlera e sotme; për një vit të mbyllur përdoren vlerat e ngrira më 1 janar (shih "Ngrirja e vitit").
- **Avokati** (filtri dhe "Në dorë"): për shifrat e çështjeve të hapura — avokati primar në fund të vitit (sot për vitin
  në vazhdim); për shifrat e çështjeve të mbyllura — avokati primar në momentin e mbylljes.

## Tabela 5 — Statistika e çështjeve (sipas gjykatës, me rreshtin "Gjithsej")

Tabela më poshtë **gjenerohet nga kodi** (`StatisticsMeasures.cs` — i njëjti burim nga i cili API-ja dhe ekrani marrin
emrat dhe përkufizimet). Mos e ndryshoni me dorë: ndryshoni kodin dhe rigjeneroni
(`UPDATE_DOCS=1 dotnet test --filter StatisticsDefinitionsDocTests`).

<!-- measures:start -->
| Shifra | Përkufizimi | Shembull |
|---|---|---|
| **Mbartur aktive** | Çështjet e regjistruara para 1 janarit dhe të pambyllura deri më 1 janar. | E regjistruar 15.12.2026, ende e hapur më 1.1.2027 → mbartur në 2027. |
| **Ardhur gjatë vitit** | Çështjet e regjistruara gjatë vitit. | E regjistruar 05.04.2027 → ardhur në 2027. |
| **Total** | Mbartur aktive + ardhur gjatë vitit. | 3 të mbartura + 1 e ardhur = 4. |
| **Aktive** | Çështjet e hapura në fund të vitit (sot për vitin në vazhdim). | E mbyllur 01.03.2027 → aktive në fund të 2026, jo në 2027. |
| **Deleguar** | Aktive, me kategori Delegim. |  |
| **Relatuar** | Aktive, me kategori Relatim. |  |
| **Aktive në ndjekje** | Aktive, as të deleguara as të relatuara. |  |
| **Përfunduar (ndjekur nga ASH)** | Të mbyllura gjatë vitit, jo të deleguara / relatuara. | Mbyllur 10.12.2026 me vendim → përfunduar në 2026. |
| **Përfunduar (deleguar / relatuar)** | Të mbyllura gjatë vitit, të deleguara ose të relatuara. |  |
| **Transferuar** | Çështjet ku avokati primar u zëvendësua (transferim) gjatë vitit. | Transferuar 01.02.2027 → 1 në 2027. |
| **Pezulluar (ASH)** | Aktive, të pezulluara, jo të deleguara / relatuara. |  |
| **Pezulluar (deleguar / relatuar)** | Aktive, të pezulluara, të deleguara ose të relatuara. |  |
| **Ankimuar / rekursuar** | Të mbyllura gjatë vitit, me vendim të ankimuar ose të rekursuar. |  |
| **Mbartur në pritje të vendimit të arsyetuar** | Të mbyllura para 1 janarit, për të cilat vendimi i arsyetuar nuk kishte ardhur deri më 1 janar. | Mbyllur 10.12.2026, vendimi i arsyetuar ende pa ardhur → 1 në 2027. |
| **Mbartur, ardhur vendimi i arsyetuar** | Të mbyllura para 1 janarit, për të cilat vendimi i arsyetuar erdhi gjatë vitit. | Mbyllur në 2026, vendimi erdhi 20.01.2027 → 1 në 2027. |
| **Dëmi i parandaluar / i shkaktuar (për çdo monedhë, dhe m²)** | Shuma e vlerave të vendimeve të çështjeve të mbyllura gjatë vitit, veçmas për çdo monedhë. | 2.500.000 ALL dhe 1.000 EUR shfaqen në dy kolona, jo si një shumë. |
| **Në dorë** | Për çdo avokat: çështjet aktive në ndjekje (jo të deleguara / relatuara) ku ishte avokat primar në fund të vitit (sot për vitin në vazhdim). |  |
<!-- measures:end -->

**Rezultati** — për çdo rezultat vendimi (Fituar, Humbur, …): sa çështje të mbyllura gjatë vitit me atë rezultat ishin
ndjekur nga ASH dhe sa ishin të deleguara / relatuara.

## Tabela 6 — Çështje institucionale

Të njëjtat shifra, vetëm për çështjet ku institucioni i zgjedhur është palë (e pafshirë) ose është i lidhur me çështjen
(monitorim ose delegim). Kur zgjidhet një **ministri**, numërohen edhe agjencitë e saj të varura drejtpërdrejt
(institucionet që e kanë atë ministri si institucion prind).

## Ngrirja e vitit

Më 1 janar (ora e Tiranës) sistemi ngrin shifrat e vitit që sapo mbaroi: ruhen për çdo çështje të dhënat e nevojshme
(përfshirë kategorinë dhe pezullimin e asaj dite). Që atëherë shifrat e atij viti lexohen nga kjo ngrirje dhe nuk
ndryshojnë, edhe nëse më vonë ndryshon kategoria ose pezullimi i një çështjeje. Një vit i mbyllur pa ngrirje llogaritet
nga të dhënat e sotme dhe shënohet **"jo e ngrirë"**. Administratori mund ta ngrijë përsëri një vit (veprimi
regjistrohet në auditim).

## Pyetje për klientin

- A janë të sakta këto përkufizime dhe emërtime (sipas memos)? (client-q-stat-definitions)
- A duhet që shifrat e publikuara të një viti të mbyllur të mos ndryshojnë kurrë? (client-q-stat-freeze)
- Kush mund të shohë cilat statistika? (Q6)

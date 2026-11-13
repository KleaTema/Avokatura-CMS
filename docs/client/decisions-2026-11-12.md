1. What I understood
A. Closing per level and archiving (7d, P1, P2)
NIVELI X/n
  ├─ pa vendim (seancat s'janë zhvilluar, s'ka përfundim) ──► AKTIVE ("në pritje")
  │                                                           → numërohet "mbartur aktive" më 1 janar
  └─ regjistrohet vendimi (rezultati) ─────────────────────► E MBYLLUR në këtë shkallë
        ├─ vendimi i arsyetuar s'ka ardhur → mbyllur, plotësohet më vonë (NUK është "mbartur aktive")
        ├─ ankimuar / jo formë e prerë     → dosja vazhdon në shkallën e radhës
        └─ FORMË E PRERË + dokumenti i vendimit i ngarkuar
                 └─► ◇ "Do të arkivohet?" ──► ARKIV (dosja mbyllet përfundimisht)

What we have now: this logic is already how 036/037 work (closing without the reasoned decision = closed; the statistics follow it). So the closing logic doesn’t change; only the archiving gets stricter.

The one change: archiving (and the “Arkivo?” question) is only allowed when the decision is final AND the decision document is uploaded. Today 038 only checks final.

B. Next level and the new “Përcjellja” feature (Answer 2, P8 + your new idea)
X/2 copies only the parties and the object. No category, no delegation (the new level gets a new lawyer). Institution links are created as MONITORING from the institution parties.
The 9 search fields in “Vazhdo çështje ekzistuese”: code, act number, court, judge, plaintiff, defendant, object, category/type, result. Built so a new field can be added easily.
New feature, the “Përcjellja e çështjes” (I deliberately call it përcjellje, not delegim, so it isn’t confused with delegation to an institution):
Zyra Durrës                                         Zyra Apel Shkodër
───────────                                         ─────────────────
Avokati mbyll X/1: rezultat + "Ankimuar"
       │
       ▼
Protokolli Durrës → tab "Për përcjellje"
  zgjedh: Shkalla e radhës ▼  Gjykata ▼
  (zyra merret automatikisht nga gjykata)
       │ Përcill
       └──────────────────────────────────────► Protokolli(t) e zyrës Apel:
                                                 • njoftim në sistem
                                                 • email sqarues
                                                 • tab "Çështjet e ardhura"
                                                 Kur vjen fletëthirrja:
                                                 "Vazhdo" → X/2 (parapopulluar)

Important to know: the “Vazhdo çështje ekzistuese” search (018) already searches every office, so the Apel Protokoll can find the case today too. Përcjellja adds what’s missing: the notification, the email and the dedicated tab. The Protokoll doesn’t have to search; the case arrives on its own.

What this needs:

A court → office mapping (which office handles which court), set by the admin.
Sending email. Locally we use Mailpit (a test inbox in Docker); in production, AKSHI’s SMTP.
Two tabs for the Protokoll: “Çështjet e mia” and “Çështjet e ardhura”.
C. Kryetar, Monitor, assistants (Answers 4, 5, P4, P5)
MONITORUESI (qendror) ── sheh TË GJITHA çështjet
        │  cakton në mbikëqyrje:  Kryetar Z ← Avokat A (+ çështjet e tij)  ose  ← çështje të veçanta
        ▼
KRYETARI I ZYRËS Z sheh/vepron mbi:
   1. çështjet e zyrës së vet
   2. çështjet/avokatët e ngarkuar nga Monitoruesi  → njoftim + tab "Në mbikëqyrje"
   3. çështjet ku vetë është avokat primar/sekondar
KRYETARI ── cakton NDIHMËSIT LIGJORË te avokatët e zyrës
NDIHMËSI ── sheh çështjet ku avokati i tij është primar (automatikisht)

What changes:

Supervision (new): the Monitor assigns a Kryetar either a lawyer (all their cases as primary) or specific cases.
Assistants: today the primary lawyer adds an assistant per case (026). Now the Kryetar attaches an assistant to a lawyer, and the assistant automatically gets every case where that lawyer is primary. I keep it per case behind the scenes (rows synced automatically), so the access code we have keeps working without a rewrite.
D. History of inaccessible levels (6e)

If you have X/2 but not X/1: you can’t open X/1, but in the history you see a summary for X/1, namely the court level, the court, the result, the decision date and the ankimuar / formë e prerë flags. No details.

E. Profile, chat, organisation chart (3a, 3b, 3e, P3, P7)
Profile: remove the phone and the notification preferences. Every notification is mandatory.
Chat:
The Protokoll joins the chat of every case they created (even after it becomes ACTIVE).
The admin has chat: they can open the chat of any case (they join when they write).
Organika (my suggestion): I’d use Excel/CSV only, from a fixed template, not diagrams.
Why not diagrams: an org chart as an image or PDF can’t be read reliably, so positions would get assigned wrongly.
The template: email (to match the user), position, unit/office, and optionally the manager.
The flow: upload → preview with validation (“42 matched, 3 emails not found, 1 duplicate”) → Apliko. Each import is kept with its history, so you can see who changed what.
Long term: if the Avokatura is in the public administration HR system (HRMIS), an integration with it would keep positions up to date automatically. That’s a question for AKSHI.
F. Reports and statistics (P6, P9)

By role:

Roli	Raporte (Tabela 1, 3)	Statistika (Tabela 5, 6)
Avokati primar	✅ çështjet e veta	✅ vetëm si primar
Avokati sekondar	✅ çështjet ku bën pjesë	❌
Ndihmësi ligjor	✅ çështjet e avokatëve të tij (+ filtër avokati)	✅ po ashtu
Juristi / Institucioni	✅ çështjet e institucionit	✅ po ashtu
Kryetari	✅ të vetat + zyra + në mbikëqyrje	✅ po ashtu
Monitoruesi / Admin	✅ të gjitha	✅ të gjitha

The page (your idea, plus my suggestions):

2–3 default filters at the top, plus “+ Shto filtër” from the list of table columns (every column can be filtered).
Dependent filters: shkalla → gjykata → gjyqtari; zyra → avokati; ministria → institucioni; kategoria → lloji. Values that would give 0 results aren’t offered.
The “Gjenero” button: nothing runs before the click (fast for large data).
Downloads: Excel, CSV and PDF. For PDF I’d use MigraDoc/PDFsharp (MIT licence), not QuestPDF, whose free licence has revenue conditions that are unclear for a state institution.
My additions:
Saved reports (“Raportet e mia”: save a filter combination and rerun it in one click).
A shareable link with the filters.
Choosing and ordering columns.
A totals row.
“Gjeneruar më … nga …” on every export.
2. Small open points, with the defaults I’ll use
#	Pika	Default (if the client says nothing)
D1	Who records the decision. You mentioned the Protokoll uploading the outcome	Stays as now: the lawyer (decision.record)
D2	Documents of X/1 for someone who only has X/2	Visible (the appeal lawyer must read the first-instance decision). Only the details of X/1 are hidden
D3	Supervision: a whole lawyer or specific cases	Both options for the Monitor
D4	Assistant: every case of the lawyer automatically	Yes, every case where the lawyer is primary
D5	Përcjellja to an office with several Protokoll users	Every Protokoll user of that office gets it

- Përcjellja: e detyrueshme në mbylljen e një shkalle jo në formë të prerë (shkalla + gjykata e radhës → zyra).
- D1: vendimin e regjistrojnë avokati primar, sekondar, ndihmësi ligjor, juristi, Kryetari. Jo Protokolli.
- D2: kush ka X/2 sheh gjithçka të X/1 (edhe dokumentet), vetëm për lexim.
- D3: mbikëqyrja si avokat i plotë ose çështje të veçanta.

3. Client correction (13.11.2026): two lawyer roles
"Avokat Shteti (primar)" (STATE_LAWYER) and "Avokat sekondar" (SECONDARY_LAWYER) are two SEPARATE system roles of the office, not only positions on a case. A user may hold both.
- Primary on a case: only a user with the role Avokat Shteti (primar). Secondary on a case: only a user with the role Avokat sekondar. The transfer swap (secondary ↔ primary) only when the person holds the needed role.
- Assistants are linked only to users with Avokat Shteti (primar).
- Statistikat: a user with Avokat sekondar and WITHOUT Avokat Shteti → no statistics (403); a user with Avokat Shteti → only the cases where they are primary at the moment the measure refers to (open → current primary, closed → primary at closing). Reports (Tabela 1/3) for both, with a "Roli im" column.
- The secondary keeps the case rights of D1 (records the decision).
- A role cannot be removed from a user who still holds that position on open cases: transfer first.
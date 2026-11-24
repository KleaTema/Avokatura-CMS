# Permissions matrix (task 053k)

Who may do what on a case, per role and the condition that gives it. Approved by the client on 17.11.2026
(docs/client/decisions-2026-11-12.md §5).

**This file is checked by a test.** `PermissionsMatrixTests` reads the table between the two `matrix` markers below. It
builds one real case per column (the condition in the header) and compares every cell with what the API actually gives:
- `permissions[]` of `GET /api/cases/{id}`, empty for a 404;
- for `chat`, the case chat's participants.

A cell may say only `●` (allowed) or `–` (not allowed). A permission the API gives that has no row here fails the test
too. Change a rule in ONE place — script / `CaseAccessService` / `v_case_access` — then change this table. Never the
other way round.

Columns, the case each one is checked on:
- **primary / secondary / assistant:** an ACTIVE case of office TR where the user holds that case role (avokat1, avokat2, ndihmes).
- **head:** the same case, for the Kryetar of its office (not a lawyer on it).
- **monitor, admin:** the same case.
- **protokoll:** a REGISTERED case the Protokoll registered (the corrections stop once a lawyer is assigned).
- **inst-mon / jurist-mon / deleg-mon:** the ACTIVE case, which the ministry MONITORS (the jurist is assigned to it).
- **inst-del / jurist-del / deleg-del:** an ACTIVE case DELEGATED to the ministry (a jurist assignment there gives nothing).

<!-- matrix:begin -->
| Permission | Avokat primar `primary` | Avokat sekondar `secondary` | Ndihmës ligjor `assistant` | Kryetari `head` | Monitoruesi `monitor` | Protokolli `protokoll` | Administratori `admin` | Institucioni (monitorim) `inst-mon` | Institucioni (delegim) `inst-del` | Juristi (monitorim) `jurist-mon` | Juristi (delegim) `jurist-del` | Inst. me delegim (delegim) `deleg-del` | Inst. me delegim (monitorim) `deleg-mon` |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| `case.view` View the case | ● | ● | ● | ● | ● | ● | ● | ● | ● | ● | – | ● | ● |
| `case.edit_registration` Correct the registration | – | – | – | – | – | ● | – | – | – | – | – | – | – |
| `party.add` Add a party | ● | ● | ● | – | – | ● | – | – | – | – | – | ● | – |
| `party.edit` Edit a party | ● | ● | ● | – | – | ● | – | – | – | – | – | ● | – |
| `party.delete` Delete a party | ● | ● | ● | – | – | ● | – | – | – | – | – | ● | – |
| `hearing.add` Add a hearing | ● | ● | ● | – | – | – | – | ● | – | ● | – | ● | – |
| `hearing.edit` Edit / complete a hearing | ● | ● | ● | – | – | – | – | – | – | – | – | ● | – |
| `document.upload` Upload a document | ● | ● | ● | – | – | – | – | ● | – | ● | – | ● | – |
| `document.delete` Delete a document | ● | – | – | – | – | – | – | – | – | – | – | – | – |
| `case.edit_data` Edit the case data | ● | ● | ● | – | – | – | – | – | – | – | – | ● | – |
| `case.change_judge` Change the judge | ● | – | – | – | – | ● | – | – | – | – | – | ● | – |
| `case.change_object` Change the object | ● | ● | ● | – | – | ● | – | – | – | – | – | ● | – |
| `case.set_category` Set the category | ● | – | – | – | – | – | – | – | – | – | – | – | – |
| `case.set_priority` Set the priority (own case) | ● | – | – | – | – | – | – | – | – | – | – | – | – |
| `case.set_priority_any` Set the priority (office) | – | – | – | ● | – | – | – | – | – | – | – | – | – |
| `case.set_status` Suspend / resume | ● | – | – | – | – | – | – | – | – | – | – | – | – |
| `case.delegate` Delegate to an institution | ● | – | – | – | – | – | – | – | – | – | – | – | – |
| `case.transfer` Transfer the lawyers | – | – | – | ● | – | – | – | – | – | – | – | – | – |
| `assistant.manage` Add / remove legal assistants (053q) | ● | – | – | – | – | – | – | – | – | – | – | – | – |
| `institution.assign_jurist` Assign the institution's jurist | – | – | – | – | – | – | – | ● | – | – | – | – | – |
| `decision.record` Record / complete the decision | ● | ● | ● | ● | – | – | – | – | – | – | – | ● | – |
| `chat.case` Case chat (by the case profile) | ● | ● | ● | – | – | – | – | ● | – | ● | – | ● | – |
| `chat` Case chat participant | ● | ● | ● | – | – | ● | – | ● | – | ● | – | ● | – |
<!-- matrix:end -->

## By state, for every column
- **`case.assign_lawyer`** (Kryetar): only while REGISTERED. From then on it is `case.transfer`.
- **`decision.close_final`** ("Arkivo"): the holders of `decision.record`, and only on the latest level when it is CLOSED, final, and has its reasoned decision with a clean document (`ArchiveRule`, `ArchiveTests`).
- **A CLOSED level** keeps: view, chat, the decision (complete it later), a VENDIM upload, and archiving.
- **An archived dossier** is view only.
- **An earlier level, seen through a later one (D2)**: view only.
- **A DELEGIM / RELATIM category** removes `case.set_category` for everyone: it changes through the delegation.
- **The Protokoll's corrections** apply only while REGISTERED, on its own registrations (task 053j) or its office's.

## Primar vs sekondar (task 053i)
- **Only the primary** may delete documents, change the judge, set the category, priority and status, and delegate.
- **Both** record and complete the decision and archive (D1), and both edit the data, parties, hearings and object.
- **On a case:** primary only with the role "Avokat Shteti (primar)"; secondary only with "Avokat sekondar".
- **Statistics:** a user with "Avokat sekondar" and without "Avokat Shteti (primar)" gets 403.

## Reports and statistics (one rule, `ReportAudience`)
| Role | Cases |
|---|---|
| Avokat primar | Where he is the primary at the measure's time (reports: also a current member). |
| Avokat sekondar | Reports: the cases where he is a current member ("Roli im"). Statistics: 403 without the primar role. |
| Ndihmës ligjor | The cases where his LINKED lawyers (assistant_lawyers, task 053e) are the primary at the measure's time. The lawyer filter is limited to them. |
| Kryetari | His office, the cases under his supervision (053d), and the cases where he holds a lawyer role. |
| Monitoruesi, Administratori | All. |
| Protokolli | None. |
| Institucioni, Juristi, Institucioni me delegim | EVERY case of the institution (a party or linked; a ministry also covers its direct agencies), whatever the lawyer (decision P6). |

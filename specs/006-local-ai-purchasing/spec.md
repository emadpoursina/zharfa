# Feature Specification: Local AI over Purchasing (Release 1, Phase 4)

**Feature Branch**: `zharfa-prd-phase-4`

**Created**: 2026-11-18

**Status**: Draft

**Input**: User description: "phase: zharfa-prd.phases.md#4 — Ship Zharfa Release 1 Phase 4 so a Manager can ask Persian purchasing questions and get cited answers from a local model on the company server (F12–F13)."

**Scope authority**: `zharfa-prd.md` v3.0, Phase 4 of `zharfa-prd.phases.md` ("Local AI over purchasing"), covering F12 (AI provider configuration) and F13 (central AI agent — purchasing) in full, plus the F10 audit events for AI questions that Phase 3 reserved until this phase. `AGENTS.md` rules bind every decision. Phase 1 (`specs/003-real-purchase-data`), Phase 2 (`specs/004-vendor-purchasing-reports`) and Phase 3 (`specs/005-permissions-audit-quality`) are existing behavior this phase uses: synced purchasing metrics, reports, roles, permissions, and the append-only audit log.

**Phase goal**: A Manager asks purchasing questions in Persian and gets cited answers from a model running on the company's own server.

## Clarifications

### Carried over from earlier phases (owner decisions, binding)

- Release 1 roles are Admin, Management, Purchasing and Finance. Vendor financials are visible to Management and Finance only. All authorization is enforced on the server; hiding something in the UI is not enforcement.
- The AI-queries permission is held by Management, Purchasing and Finance; Admin does not hold it (Phase 3 FR-002). Admin holds AI provider settings as part of administration.
- Until Phase 6, the reserved vendor-financials area answers "data unavailable" to permitted users and 403 to everyone else; no vendor-balance figure exists yet.
- Amounts are shown in Rial, labeled "ریال". Jalali calendar, Persian RTL, Persian digits.
- Simulator example columns and objects use `Sim*` names; real Mabna data never enters the repository; development uses `deploy/mabna-sim/`.
- Each install is exactly one data mode. Production shows real data only; demo installs label everything as demonstration data. The AI uses one dataset per install and never mixes real and synthetic figures.
- Phase 1 hid and refused the AI agent in production until this phase. This phase turns that on for purchasing questions only.
- Audit entries never contain secrets, passwords, provider keys, database credentials, or result rows. Phase 3 reserved `settings.ai_provider.changed` and AI-question events for this phase.
- Source-item status rules are unchanged (`unknown`, `candidate`, `confirmed`, `corrected`; production uses only `confirmed` or `corrected`). A figure that cannot be computed shows "data unavailable", never an estimate.
- Purchases, payments and vendor balance stay separate formulas; a price difference is never presented as profit.

### Assumptions taken in place of questions (no-question run; skip)

- **Local is the default.** A fresh install uses the local model on the company server. The optional cloud provider stays off until an Admin enables it with a recorded owner-approval note.
- **Keys stay in server secrets.** Connection details for providers (including any key) remain in server environment or Docker secrets. The in-app Settings → AI page shows redacted status (provider name, whether it is default/enabled, connection-test result) and never a key. Admins do not type keys into the application.
- **Owner-approval note.** Enabling the cloud provider requires a non-empty Persian or free-text note naming the owner and the reason. The note is stored with the change and written to the audit log. Disabling the cloud provider does not require a new note. Re-enabling after disable requires a new note.
- **What "aggregates only" means for cloud.** When the cloud provider is on, every payload sent to it contains only already-summarized purchasing metrics (totals, counts, ranked names with amounts) that the same user could see in reports — never document lines, full result tables, identifiers beyond vendor/product names already on reports, or personal data. A captured sample of one such request is part of the phase exit evidence.
- **Fixed 20-question Persian purchasing test set.** The set is a committed fixture of exactly 20 Persian questions over Phase 1–2 purchasing metrics (company totals, previous-period comparison, monthly trend, vendors, products, categories, returns, last-sync / data mode). It includes questions whose correct answer is "data unavailable" (for example no sync yet, unverified source, or out-of-scope sales/inventory). It includes at least one vendor-balance / payable question used to prove Purchasing sees no financial figures. Expected answers cite the same period, filters and source metric as the matching report, or say "data unavailable". Demo 1 sales/debt/stock questions are not in this set.
- **Benchmark.** The procedure runs that same fixed set (or the subset used for scoring) against two or three candidate local models on the target server, records accuracy (share of answers that match the expected cited result or "data unavailable") and response time, and writes a dated record. The default local model is the candidate with highest accuracy; ties break to the faster median response time. The record is how the default is chosen; operators do not pick a default by taste. Target for a typical answer is under 30 seconds (PRD §8); if no candidate meets it, the record still names a default local model and states the measured times so hardware or cloud fallback can be decided later — the product default remains local.
- **Answers in this phase are cited text.** Journey 1's table/chart in an AI answer is Phase 5 (F14). Phase 4 answers state figures in Persian prose (or equivalent structured text) with period, filters and source metric. Users cannot save an AI answer as a report in this phase.
- **Approved purchasing tools only.** The agent may read only the same purchasing metrics the reports already compute (net purchases, gross, returns, VAT, previous-period comparison, monthly trend, active vendors, top vendors and products, by vendor / product / category, most-purchased products, purchase returns, transaction-history summaries). It cannot invent SQL, cannot read Mabna, cannot write anything, and cannot call vendor-financial tools until Phase 6 (and then only for roles that hold that permission).
- **Limits and timeouts.** Each approved tool call is capped at 100 result rows and 10 seconds. Exceeding the row cap yields "data unavailable" for that metric; exceeding the tool timeout or the 30-second user-visible answer budget yields the assistant-unavailable message — never a partial guess.
- **Facts vs interpretation.** Numeric facts come first, labeled with period, filters and source metric. Any commentary is clearly separate and must not introduce numbers that the tools did not return.
- **Unavailable vs zero vs provider down.** No purchases in a valid synced period is zero (same as reports). Missing or unverified source, no successful sync, or a question the tools cannot answer is "data unavailable". A stopped, unconfigured or timed-out provider is a distinct Persian message that the assistant is unavailable; Overview and reports still work.
- **Who can ask.** Management, Purchasing and Finance see the assistant and may ask. Admin does not hold AI-queries; the assistant is refused for Admin (403 on a direct call) and is not offered in Admin navigation. A caller without a session is refused as not signed in.
- **Purchasing and vendor balances.** A Purchasing user who asks about vendor balance, payables, cheques or payments receives no financial figures: the tools do not return them, and the answer says the user cannot see that data or that it is unavailable — never a number. Management and Finance asking the same question in this phase also get no figures, because vendor financials are not implemented until Phase 6 ("data unavailable").
- **Audit of AI questions.** One append-only entry per question: user, time, action, and the question text (trimmed to a bounded length). Period and filters are recorded when the agent used them. Tool names may be recorded. Tool results, figures, keys and full prompts that would embed result rows MUST NOT be stored. Enabling/disabling cloud and connection tests are administrative changes (`settings.ai_provider.changed` or equivalent), without keys.
- **Demo-mode Demo 1 assistant.** Demo 1 sales/inventory/receivables Q&A remains only on demo-mode Demo 1 pages. Production-mode AI is the purchasing assistant in this spec. Demo-mode purchasing AI is labeled as demonstration data.
- **PR #3 (amount names)** is out of scope; this phase does not rename amount fields.

### Session 2026-11-18

- Q: Should each purchasing question be answered on its own, or may the model use earlier turns in the same visit as context? → A: Independent. The model does not see earlier questions or answers; a question that omits a period uses the Overview default (current Jalali month).
- Q: After an Admin enables the cloud provider with an owner-approval note, which provider answers until they turn it off? → A: Cloud is the sole answering provider until disabled. Local is unused until cloud is turned off. Cloud failure yields the assistant-unavailable message; the product does not silently switch host.
- Q: What row cap and timeouts apply to each approved purchasing tool call and to the whole answer? → A: At most 100 result rows per tool call, 10 seconds per tool call, 30 seconds for the full user-visible answer.
- Q: What happens if the same signed-in user sends another question while one answer is still running? → A: The second request is refused with a Persian message to wait; other users may still ask at the same time.
- Q: After a user leaves the assistant page, should earlier questions and answers still be available in the product UI? → A: No saved chat. The page may show the current question and answer until the user leaves; later they appear only in the Admin audit log (question text, not answers).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Manager asks purchasing questions in Persian (Priority: P1)

A Management user opens the AI Assistant, asks a purchasing question in Persian (for example which vendor's purchases grew most this quarter, or this month's net purchases), and receives an answer in Persian. The answer uses the same purchase definitions as the Overview and reports, states the period, filters and source metric, and matches the figures those reports would show for the same period and filters. If the needed data is missing or incomplete, the answer is "data unavailable" and does not estimate.

**Why this priority**: This is the Phase 4 goal and Journey 1 step 5 (without tables/charts). It is the AI accuracy metric in the PRD.

**Independent Test**: After a successful simulator sync, sign in as Management, ask the 20-question Persian fixture; confirm every answer is correct and cited, or says "data unavailable", and that cited figures match the corresponding report for the same period and filters.

**Acceptance Scenarios**:

1. **Given** a signed-in Management user and a successful purchasing sync, **When** they ask a Persian question that the purchasing reports can answer, **Then** the assistant answers in Persian, states period, filters and source metric, and the figures match the same report for those filters.
2. **Given** the 20-question Persian purchasing test set, **When** each question is asked as a user who holds the AI-queries permission, **Then** every answer is correct and states period, filters and source metric, or says "data unavailable" without guessing.
3. **Given** a question whose data is missing, unverified, out of purchasing scope (sales, inventory, warehouse), or not computable from approved tools, **When** it is asked, **Then** the answer is "data unavailable" (or an equivalent Persian phrasing that data is not available) and contains no invented figure.
4. **Given** a selected period with no purchases, **When** the user asks for that period's purchase total, **Then** the answer is zero (not "data unavailable"), consistent with the Overview.
5. **Given** a demo-mode install, **When** the assistant answers, **Then** the answer is labeled as demonstration data and uses only the demo dataset.
6. **Given** a production-mode install, **When** the assistant answers, **Then** it uses only that install's real synced purchasing data and never Demo 1 sales, inventory or receivables figures.
7. **Given** an answer that includes both a figure and a comment, **When** a reader looks at it, **Then** the figure is presented as a fact with its citation, and the comment is visibly separate and introduces no extra numbers.
8. **Given** a second question in the same visit that omits the period, **When** it is asked, **Then** the assistant cites the Overview default period (current Jalali month) and does not reuse the previous question's period or figures.
9. **Given** a permitted user who already has a question in flight, **When** they send another question, **Then** the second request is refused with a Persian wait message and no second answer is generated.
10. **Given** a user who received an answer and then leaves the assistant page, **When** they return, **Then** the previous answer is not restored in the UI; the Admin can still find the question text in the audit log.

---

### User Story 2 - Local model is the default on the company server (Priority: P1)

The assistant runs on a local model on the company server by default, through the same provider arrangement already used for AI. An Admin opens Settings → AI, sees that the local provider is the default, and can run a connection test that reports success or a failure reason without showing any key.

**Why this priority**: F12 AC1 and Journey 4; real company data stays on the server unless the owner later approves otherwise.

**Independent Test**: On a fresh install with only local provider configuration, ask a purchasing question as Management and confirm it is answered by the local default; as Admin, open Settings → AI, run a connection test, and confirm the page shows local as default and never a key.

**Acceptance Scenarios**:

1. **Given** a fresh install, **When** anyone uses the assistant, **Then** the local model on the company server is the provider in use unless an Admin has enabled the cloud provider with an owner-approval note; while cloud is enabled it is the only answering provider.
2. **Given** an Admin, **When** they open Settings → AI, **Then** they see the local provider marked as default, its redacted status (name and whether connected, never a key), and a connection-test action.
3. **Given** an Admin, **When** they run a connection test, **Then** the page reports success or a Persian failure reason and exposes no key in the page, the response, or the logs.
4. **Given** a Management, Purchasing or Finance user, **When** they open Settings → AI or call its actions directly, **Then** the server answers 403 and no provider details beyond what a non-admin may already see are returned.
5. **Given** the frontend bundle, any API response and the application logs from a test session, **When** they are inspected for provider keys, **Then** none are present.

---

### User Story 3 - Operator benchmarks candidate models and records the default (Priority: P1)

An operator (Admin) runs the documented benchmark: the fixed Persian purchasing question set against two or three candidate local models on the target server. The run records accuracy and response time for each candidate. The default local model is the one chosen from that record.

**Why this priority**: Phase 4 exit criterion and F12 AC2; it is how the company knows the default is evidence-based.

**Independent Test**: Run the documented benchmark procedure against two or three candidates; confirm a dated record lists accuracy and response time per candidate and names exactly one default; confirm a subsequent assistant question uses that default.

**Acceptance Scenarios**:

1. **Given** two or three candidate local models available on the target server, **When** the benchmark procedure is run, **Then** it executes the fixed Persian purchasing question set on each and writes accuracy and response time per candidate.
2. **Given** a completed benchmark record, **When** the default is selected, **Then** it is the candidate with the highest accuracy, with faster median response time breaking ties, and that choice is written in the record.
3. **Given** the chosen default, **When** a Management user asks a purchasing question, **Then** the assistant uses that default local model.
4. **Given** the benchmark record, **When** a reviewer reads it, **Then** they can see enough to repeat the run (question set identity, candidates, date, accuracy, times, chosen default).

---

### User Story 4 - Permissions apply to every AI question (Priority: P1)

The assistant only returns what the asking user's role can already see in reports. A Purchasing user who asks about vendor balances, payables or payments gets no financial figures. Admin cannot use the assistant. Direct calls without permission are refused on the server.

**Why this priority**: Project rule (server-side authorization) and the explicit Phase 4 exit criterion for Purchasing vs vendor balances.

**Independent Test**: As Purchasing, ask the vendor-balance question from the test set and a normal purchase-total question; confirm the first returns no financial figures and the second is answered. As Admin, confirm the assistant is refused. As Management, confirm a purchase question is answered.

**Acceptance Scenarios**:

1. **Given** a Purchasing user, **When** they ask about vendor balances, payables, cheques or payments, **Then** the answer contains no financial figures and does not leak a balance, payment or cheque amount.
2. **Given** a Purchasing user, **When** they ask a purchasing-volume question they are allowed to see in reports, **Then** they receive a cited answer (or "data unavailable") like any other permitted user.
3. **Given** an Admin user, **When** they open the assistant or call its API, **Then** the server answers 403, no answer is generated, and the attempt is audit-logged as access denied.
4. **Given** a caller who is not signed in, **When** they call the assistant, **Then** the server answers 401 and returns no data.
5. **Given** Management or Finance, **When** they ask about vendor balances in this phase, **Then** the answer is "data unavailable" (financials are not in this release yet) and still contains no invented figure.
6. **Given** any role, **When** a tool would return data that role cannot see, **Then** the tool is not executed for that data and the assistant never includes it.

---

### User Story 5 - Admin may enable the cloud provider only with owner approval (Priority: P2)

An Admin who wants the optional cloud provider opens Settings → AI, enters an owner-approval note, and enables it. The screen states that only aggregated results are sent. A captured request to the cloud provider shows aggregates only — not raw rows or personal data. Keys never appear.

**Why this priority**: F12 AC3 and privacy NFR; required for the phase exit, but the product works with local-only if the owner never approves cloud.

**Independent Test**: As Admin, refuse enablement without a note; enable with a note; capture one cloud-bound request from a purchasing question and confirm it contains only aggregates; confirm the audit log has the enablement with the note and no key.

**Acceptance Scenarios**:

1. **Given** the cloud provider is off, **When** an Admin tries to enable it without an owner-approval note, **Then** enablement is refused and the local provider stays in use.
2. **Given** an Admin, **When** they enable the cloud provider with a non-empty owner-approval note, **Then** the setting is saved, the note is stored, the screen states that only aggregated results are sent, and an audit entry records the change (user, time, action, that an approval note was recorded) without the provider key.
3. **Given** the cloud provider is enabled, **When** a permitted user asks a purchasing question, **Then** that answer is produced by the cloud provider (local is unused), and a captured request toward the cloud provider contains only aggregated tool results, never raw document rows or personal data.
4. **Given** the cloud provider is enabled, **When** an Admin turns it off, **Then** the assistant returns to the local default without requiring a note.
5. **Given** a non-Admin, **When** they try to enable the cloud provider, **Then** the server answers 403.

---

### User Story 6 - A stopped provider does not take down dashboards (Priority: P1)

If the AI provider is stopped, unreachable, unconfigured or times out, the assistant tells the user in Persian that it is unavailable. Overview, vendor reports and other pages keep working with the same figures as before.

**Why this priority**: F13 AC5 and the Phase 4 exit criterion; the assistant is optional relative to daily report use.

**Independent Test**: Stop the AI provider process, open Overview and a purchasing report (they load), ask a question (Persian unavailable message, no stack trace or key), then start the provider again and confirm a question is answered.

**Acceptance Scenarios**:

1. **Given** the AI provider is stopped or unreachable, **When** a permitted user asks a question, **Then** the assistant reports in Persian that the service is unavailable (not "data unavailable" for a missing metric) and shows no key, stack trace or internal host details.
2. **Given** the same outage, **When** any user opens the Overview or a purchasing report, **Then** those pages load and show their usual figures and last-sync time.
3. **Given** a provider timeout while answering, **When** the wait exceeds 30 seconds (or a tool exceeds 10 seconds), **Then** the user is told the assistant did not finish, and no partial guessed figures are shown.
4. **Given** the provider is restored, **When** the user asks again, **Then** a normal cited answer is returned without restarting the rest of the application.

---

### User Story 7 - AI questions appear in the audit log without secrets (Priority: P2)

After people use the assistant, the Admin filters the audit log and sees each AI question with user, time and the question text (bounded), without result rows or keys. Provider-setting changes appear as well.

**Why this priority**: F10 AC1 includes AI questions; Phase 3 deferred the event until the action existed.

**Independent Test**: Ask several questions as Management and Purchasing (including a refused Admin call and a Purchasing vendor-balance question), change the cloud setting as Admin, then as Admin filter the audit log by AI-question and settings actions; confirm entries and confirm no figures, keys or result rows.

**Acceptance Scenarios**:

1. **Given** a permitted user asks a question, **When** the Admin views the audit log, **Then** an entry shows the user, Jalali date and time, that an AI question was asked, and the question text (trimmed if long), with no figures or tool result rows.
2. **Given** an Admin enables or disables the cloud provider or runs a connection test, **When** the audit log is viewed, **Then** each action appears as an administrative change without any key.
3. **Given** an access-denied attempt to the assistant, **When** the audit log is viewed, **Then** it is recorded like other 403s (user, time, refused target, missing permission).
4. **Given** any audit entry or log line from this phase, **When** it is inspected, **Then** it contains no provider key, credential, or result rows.
5. **Given** a non-Admin, **When** they open the audit log, **Then** the server still answers 403 (unchanged from Phase 3).

---

### Edge Cases

- Provider stopped while a question is in flight: the user receives the unavailable message; dashboards stay up.
- Two users ask at the same time: each answer uses that user's permissions and the install's single dataset; one user's question cannot see the other's results.
- Same user sends a second question while the first is still running: the second request is refused with a Persian wait message; the first continues.
- Question uses Arabic ي/ك in a vendor name: matching follows the same ی/ک normalization as vendor search (Phase 2).
- Question omits a period: the assistant uses the same default period as the Overview (current Jalali month) and cites that period. Earlier questions in the same visit are not used as context.
- Question asks for a custom range or previous Jalali month/quarter/year: citations match Phase 2 period-comparison rules.
- Question asks the agent to "ignore permissions" or to query Mabna directly: refused; only approved purchasing tools run; no extra data is returned.
- Question asks for a table, chart, saved report or Excel file: this phase answers with cited text (or "data unavailable"); it does not generate tables/charts or save/export (Phase 5).
- Cloud enabled but the cloud endpoint fails: Persian unavailable message; local is not used while cloud is enabled; the product does not silently send data to another host.
- User leaves the assistant page: the current exchange is discarded from the UI; answers are not persisted as a chat; only the bounded question text remains in the Admin audit log.
- Owner-approval note of only whitespace: treated as missing; enablement refused.
- Very long question: stored in the audit log only up to the bounded length; the model still sees a bounded prompt; no result rows are stored.
- Audit write fails after a question: the user still receives the answer (Phase 3 FR-018); the failure goes to the application log without secrets.
- No successful sync yet: purchasing questions answer "data unavailable"; Overview already shows the same.
- Production-mode user tries Demo 1 sales/inventory AI: those endpoints stay hidden and refused; the purchasing assistant does not answer sales or stock from Demo 1 data.
- Demo-mode purchasing AI and Demo 1 AI both exist: each is labeled demonstration data; they still do not mix datasets in one answer.
- Candidate model missing during benchmark: that candidate is recorded as failed/unavailable, not as a silent zero accuracy that could win the default.
- Local model too slow: times are recorded; default remains a local model; cloud is not auto-enabled.

## Requirements *(mandatory)*

### Functional Requirements

**Local default and provider configuration (F12)**

- **FR-001**: The system MUST provide a local model provider behind the existing provider interface and MUST use it as the default provider on a fresh install and whenever the cloud provider is not enabled. WHILE the cloud provider is enabled, the system MUST use it as the sole answering provider. A cloud failure MUST produce the assistant-unavailable message and MUST NOT silently switch to local or to another host.
- **FR-002**: The system MUST keep provider keys and credentials in server environment or Docker secrets only. They MUST NOT appear in the frontend bundle, repository, API responses, audit entries or application logs.
- **FR-003**: An Admin MUST be able to open Settings → AI and see redacted provider status (which provider is default, whether each is enabled/connected, model identity as configured) with no key material.
- **FR-004**: An Admin MUST be able to run a connection test for a configured provider. The result MUST be success or a failure reason and MUST NOT expose keys.
- **FR-005**: Non-Admin callers of AI provider settings MUST receive 403 with no key material. The attempt MUST be audit-logged as access denied.

**Benchmark (F12)**

- **FR-006**: The system MUST include a documented, repeatable benchmark procedure that runs the fixed Persian purchasing question set against two to three candidate local models on the target server and records accuracy and response time per candidate, plus the date and the chosen default.
- **FR-007**: The default local model MUST be taken from that record: highest accuracy, then faster median response time. Operators MUST NOT select a default that is absent from the record.

**Cloud opt-in (F12)**

- **FR-008**: The cloud provider MUST be off by default. Enabling it MUST require a non-empty owner-approval note. The Settings → AI screen MUST state that only aggregated results are sent.
- **FR-009**: While the cloud provider is enabled, every request sent to it MUST contain only aggregated purchasing tool results the asking user is allowed to see — never raw document rows, line-level data, or personal data.
- **FR-010**: Disabling the cloud provider MUST return the assistant to the local default and MUST NOT require a note. Re-enabling MUST require a new note.
- **FR-011**: Cloud enablement, disablement and connection tests MUST be written as administrative audit events without keys. The owner-approval note MAY be stored with the setting and referenced in the audit entry without embedding secrets.

**Purchasing assistant (F13)**

- **FR-012**: In production mode, the purchasing AI assistant MUST be available to roles that hold the AI-queries permission (Management, Purchasing, Finance). The Phase 1 production hide-and-refuse rule is lifted for this assistant only. Demo 1 sales/inventory/receivables AI MUST remain hidden and refused in production.
- **FR-013**: When a permitted user asks a question, the system MUST answer only through approved read-only purchasing tools/views, with an allowlist. Each tool call MUST return at most 100 result rows and MUST time out after 10 seconds. The user-visible wait for a full answer MUST be at most 30 seconds. Exceeding the row cap MUST yield "data unavailable" for that metric; exceeding a timeout MUST yield the assistant-unavailable message and MUST NOT return a partial guess. The agent MUST NOT write data, MUST NOT query Mabna, MUST NOT run arbitrary queries, and MUST NOT use tools outside the purchasing allowlist.
- **FR-014**: Every answer MUST state the period, filters and source metric used, or MUST say "data unavailable". The system MUST NOT estimate or invent figures.
- **FR-015**: Figures in answers MUST use the same documented purchase definitions as the Overview and purchasing reports (net, gross, returns, VAT kept separate). Purchases MUST NOT be presented as payments or vendor balance. Price differences MUST NOT be presented as profit. Facts MUST be kept separate from interpretation.
- **FR-016**: The assistant MUST apply the asking user's permissions to every tool call. Data the user's role cannot see in reports MUST NOT appear in the answer.
- **FR-017**: A Purchasing user asking about vendor balances, payables, payments or cheques MUST receive no financial figures.
- **FR-018**: Admin MUST NOT hold the AI-queries permission. Direct assistant calls as Admin MUST return 403 with no answer body derived from tools.
- **FR-019**: When the AI provider is unavailable, unconfigured or times out, the system MUST tell the user in Persian that the assistant is unavailable and MUST leave Overview, reports and other non-AI pages unaffected.
- **FR-020**: "Data unavailable" is for missing, unverified, incomplete or out-of-scope metrics. A valid period with no purchases MUST be answered as zero, consistent with reports. Provider failure MUST use the unavailable-service message, not a guessed number.
- **FR-021**: The assistant MUST use only the install's dataset and MUST NOT combine real and synthetic figures. Demo-mode answers MUST be labeled as demonstration data.
- **FR-022**: On the committed 20-question Persian purchasing test set, every answer MUST be correct and MUST state period, filters and source metric, or MUST say "data unavailable".
- **FR-023**: Answers in this phase MUST NOT include generated tables or charts and MUST NOT save or export a report (Phase 5).
- **FR-030**: Each question MUST be answered independently. The model MUST NOT receive earlier questions, answers or tool results as context. A question that omits a period MUST use the Overview default (current Jalali month) even if a previous question in the same visit named a different period.
- **FR-031**: The system MUST allow at most one in-flight assistant question per signed-in user. A further question from that user MUST be refused with a Persian wait message and MUST NOT start a second model run. Other users MAY ask at the same time, each under their own permissions.
- **FR-032**: The system MUST NOT persist a chat history of questions and answers in the assistant UI. The page MAY show the current exchange until the user leaves. After leaving, previous answers MUST NOT be restored. The Admin audit log remains the record of question text (not answers).

**Audit (F10, this phase's events)**

- **FR-024**: Each AI question from a signed-in user MUST write an append-only audit entry with user, time, action and target (the question text, trimmed to a bounded length; period and filters when used). Entries MUST NOT contain tool result rows, figures, provider keys or credentials.
- **FR-025**: Existing Phase 3 audit behavior (filters, append-only, no secrets, 403 for non-Admin) MUST remain. AI-question and AI-settings actions MUST be filterable by the Admin with Persian labels.

**Shared rules**

- **FR-026**: The assistant and Settings → AI MUST follow Persian-first conventions: RTL layout, Jalali dates and times, Persian digits, Persian labels, and the install's data-mode label.
- **FR-027**: Nothing in this phase MAY write to Mabna or read Mabna's `HOST_ID()`-filtered report views on live Mabna. Reports continue to read the synced copy only.
- **FR-028**: Real company data MUST NOT enter the repository. Benchmark records committed to the repo MUST use simulator/demo questions and timings, or pass/fail only — never live row extracts.
- **FR-029**: Keyboard use and WCAG 2.1 AA contrast apply to the assistant and Settings → AI, consistent with the rest of the product.

### Key Entities

- **AI provider**: a configured way to run the assistant (local default on the company server, optional cloud). Attributes: identity, enabled/default flag, redacted status, connection-test result. Credentials live only in server secrets.
- **Owner-approval note**: non-empty text recorded when the cloud provider is enabled; identifies owner approval and reason; stored with the setting; never a key.
- **Purchasing tool / approved view**: a read-only, allowlisted metric the agent may call (period, filters, limits, timeout); same definitions as reports; no raw Mabna access.
- **AI question**: a user's Persian prompt, answered independently of earlier turns; audited as text (bounded); answered with cited facts or "data unavailable" or a service-unavailable message. At most one in-flight question per user.
- **AI answer**: Persian response containing cited figures (period, filters, source metric) and optional clearly separated interpretation; no tables/charts in this phase; not persisted as chat history.
- **Persian purchasing test set**: committed fixture of 20 questions with expected cited answers or "data unavailable", used for the accuracy gate and the benchmark.
- **Benchmark record**: dated result of running the test set on two or three local candidates: accuracy, response times, chosen default.
- **Audit entry**: existing Phase 3 entity, extended with AI-question and AI-settings actions; still never secrets or result rows.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: On the 20-question Persian purchasing test set, 20 of 20 answers are correct and state period, filters and source metric, or say "data unavailable" (PRD AI accuracy metric; Phase 4 exit).
- **SC-002**: A typical cited purchasing answer from the default local model is returned in under 30 seconds in the benchmark record (PRD §8 target), or the record states the measured times when the target is missed.
- **SC-003**: The benchmark record covers two or three candidate local models with accuracy and response time each, and names exactly one default that matches FR-007.
- **SC-004**: With the cloud provider left at default (off), 100% of assistant answers in a fresh-install test use the local default. After enablement with an owner-approval note, 100% of subsequent answers in that test use the cloud provider until it is disabled.
- **SC-005**: In a captured cloud-bound request after enablement with an owner-approval note, 100% of payload content is aggregated tool results; zero raw document rows or personal fields are present. Enablement without a note succeeds 0 times.
- **SC-006**: When the AI provider is stopped, 100% of Overview and purchasing-report page loads in the test still succeed, and 100% of assistant questions return a service-unavailable message with no guessed figures.
- **SC-007**: A Purchasing user asking the vendor-balance item of the test set receives zero financial figures (no balance, payment or cheque amounts).
- **SC-008**: In a test session of AI questions, provider setting changes and a connection test, 100% of those actions appear in the audit log with user, time and action, and a scan of the frontend bundle, API responses, audit entries and application logs finds zero provider keys and zero tool result rows.
- **SC-009**: Direct assistant calls as Admin and as a signed-out caller return no answer data (403 and 401 respectively); 100% of Admin attempts are audit-logged as access denied.
- **SC-010**: Cited figures in sampled answers (at least five questions that have a matching report) equal the corresponding Overview or purchasing-report figures for the same period and filters (100% match).
- **SC-011**: While one question is in flight for a user, 100% of further questions from that user are refused with a wait message; a second user can still receive an answer in the same window.
- **SC-012**: After leaving and returning to the assistant, 0 restored prior answers appear in the UI; the corresponding question texts remain in the Admin audit log.

## Assumptions

- **Dependencies**: Phase 1 synced purchasing copy, metric definitions and data-mode rules; Phase 2 reports and ی/ک search; Phase 3 roles, AI-queries permission, audit log, and reserved vendor-financials "data unavailable"; Demo 1 provider interface and secret-redaction pattern; `deploy/mabna-sim/` for off-site proof.
- **Default period**: current Jalali month, same as Overview, when the user does not name a period. Independent questions mean this default is used even after a prior question named another period.
- **No assistant chat store**: audit holds question text; answers are not a saved conversation.
- **Test set ownership**: the 20 questions live in the repo as a fixture; expected answers are computed from the same metric definitions as reports on simulator data, not from a second handwritten number sheet that can drift.
- **Cloud capture**: "captured request" for the exit criterion is produced in a controlled test (demo/simulator) so real Mabna data never leaves the site or enters the repo.
- **Hardware**: the company LAN server is the intended place to run the local model; development may run the same procedure against a smaller local model as long as the committed benchmark format is the same.
- **Admin and AI**: Admin configures providers and does not ask questions (Phase 3 matrix). If the owner later wants Admin to ask, that is a permission-set change outside this phase.
- **PR #3**: amount display names on master are not changed here.

## Out of Scope

- Phases 1–3 and 5–7 work; their PRs and specs are not reopened.
- Tables and charts in AI answers, saving those answers, persisted assistant chat history, and Excel export (Phase 5: F14, F15, F16).
- Vendor financial data and vendor balance on the profile (Phase 6: F17, F5 AC3); this phase only withholds figures and answers "data unavailable".
- LAN production deployment, backups and go-live (Phase 7: F18).
- Sales and inventory questions on real data (Releases 2–3: F19–F23, F25–F29); Demo 1 sales/inventory AI remains demo-mode only.
- Merging the phase pull request (operator merges after UAT).
- PR #3 amount-name changes.
- Platform Assistant, MCP tool catalog, AI anomaly/pattern engine (F31), Metabase, Wren AI.
- Remote/VPN access, native mobile, PWA, English UI.
- Any write to Mabna, live per-request queries to Mabna, and reading `HOST_ID()`-filtered report views on live Mabna.
- Deferred items F19–F33 as listed in `zharfa-prd.phases.md`.

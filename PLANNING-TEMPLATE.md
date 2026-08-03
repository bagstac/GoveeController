# AI-Optimized Planning Template

> **Purpose:** A reusable template for creating implementation plans that are easy for AI agents to
> understand, execute, and verify. Use this for any feature, bug fix, or improvement.

---

## Template

```markdown
## Plan: {Title (2–10 words)}

**TL;DR** — What we're building, why, and the recommended approach in 1–2 sentences.

**Status:** 🔴 Not started / 🟡 In progress / 🟢 Done · **Branch:** `feature/xyz` · **Date:** YYYY-MM-DD

---

## Phases

### Phase 1: {Phase name} *(blocks nothing — can start immediately)*

**Steps**
1. {Step description} — *parallel with step 2*
2. {Step description} — *parallel with step 1*
3. {Step description} — *depends on steps 1 and 2*

**Relevant files**
- `path/to/file.cs` — {what to create or modify, referencing specific functions/types/patterns}

**Verification**
1. `dotnet build` — must produce 0 warnings, 0 errors
2. `dotnet test` — all tests pass
3. {Manual verification step, e.g., "Visit /devices and confirm cards render"}

---

### Phase 2: {Phase name} *(depends on Phase 1)*

**Steps**
…

---

## Decisions *(settled — do not relitigate)*

| Question | Decision | Rationale |
|---|---|---|
| {Design choice} | {Chosen answer} | {Why} |

---

## Scope Boundaries

**In scope:**
- {What this plan covers}

**Deliberately excluded:**
- {What this plan does NOT cover, and why}

---

## Verification Checklist *(run after the whole plan)*

- [ ] `dotnet build` — 0 warnings, 0 errors
- [ ] `dotnet test` — all passing
- [ ] {Integration test or manual smoke test}
- [ ] {Docker / deploy verification if applicable}

---

## Further Considerations *(1–3 items maximum)*

1. {Open question or risk, with a recommended approach}
```

---

## Rules for writing plans AI agents can execute

### 1. Structure for scannability

- **TL;DR first.** The agent needs to know what it's building before it reads details.
- **Phases over flat lists.** Group steps into independently verifiable phases. Each phase header
  states what it blocks and what blocks it.
- **Dependencies explicit.** Every step says whether it's parallel with another step, or what it
  depends on. Never make the agent infer ordering.

### 2. Be surgically precise about files and symbols

- **Full paths always.** `src/GoveeController.Application/Shortcuts/ShortcutService.cs`, never
  "ShortcutService.cs".
- **Reference specific symbols.** "Modify `ApplyShortcutAsync` (~line 100)" not "fix the shortcut
  service". The agent should know exactly which function/class/type to touch.
- **Describe changes, don't write code.** Say "add a nullable `NextShortcutId` FK property on
  `Shortcut`" not "here's the C#". The executing agent writes the code; the plan describes intent.

### 3. Verification must be concrete and exhaustive

- **Specific commands.** `dotnet build`, `dotnet test --filter "ShortcutServiceTests"`, not "run
  tests".
- **Specific URLs.** `http://localhost:8080/shortcuts`, not "check the shortcuts page".
- **Specific assertions.** "Assert `TurnOffAsync` was called for devices 1 and 3, and
  `ShortcutApplyException` is thrown with 1 failure", not "make sure it handles errors".
- **Cover both automated and manual.** Unit tests + integration smoke tests + real-hardware
  validation when applicable.

### 4. Settle decisions upfront

- Put design choices in a **Decisions** table with rationale. This prevents the executing agent
  from second-guessing settled trade-offs.
- Mark them explicitly: "settled — do not relitigate."
- If a decision *could* go another way, put it in **Further Considerations** instead.

### 5. Define scope boundaries

- **In scope:** prevents the agent from under-delivering.
- **Deliberately excluded:** prevents scope creep. Every excluded item states *why* it's excluded,
  so the agent doesn't "helpfully" add it.

### 6. Keep it linear, not branching

- One recommended path. If there are alternatives worth mentioning, put them in **Further
  Considerations** with a clear recommendation.
- Don't say "you could do X or Y." Say "do X because Z. Y was considered but rejected because W."

### 7. Ground rules for the executing agent

Include a short section at the top with non-negotiable constraints:
- Build and test after every step (or every phase, for larger plans).
- Don't commit secrets.
- Preserve existing conventions (comment style, naming, error-handling patterns).
- Any hardware-specific testing requirements.

### 8. Track progress with checkboxes

- Use `- [ ]` checkboxes for every step. The executing agent checks them off as it goes.
- If a session times out, the next agent reads the plan and resumes from the first unchecked box.
- Put the plan on disk (`plan.md` in the workspace root) as the durable copy; also keep it in
  session memory (`/memories/session/plan.md`).

### 9. Include a handoff section for long-running plans

At the end of the plan, include:

```markdown
## Handoff *(for resuming after session loss)*

- **Last completed step:** {step number and description}
- **Current state:** build passes / tests pass / known issue: {description}
- **Next action:** {exactly what to do next}
- **Branch:** `feature/xyz`
```

### 10. Anti-patterns — what NOT to do

| ❌ Don't | ✅ Do |
|---|---|
| "Fix the bug in the service" | "In `ShortcutService.ApplyShortcutAsync` (~line 100), wrap each `ApplyToTargetAsync` call in try/catch and collect failures" |
| "Add tests" | "Add `ShortcutServiceTests.ApplyShortcut_ContinuesAfterDeviceFailure`: 3 targets, mock throws for target 2, assert targets 1 and 3 were called and exception carries 1 failure" |
| "Run the app and check it works" | "`docker compose up --build -d`, then visit `http://localhost:8080/shortcuts`, click Apply on 'All Off', confirm all 6 bulbs turn off" |
| "Maybe consider X or Y" | "Do X. Y was rejected because Z." |
| Code blocks in the plan | Descriptions of changes with file paths and symbol names |
| Vague phase names: "Backend work" | "Phase 2: Chain validation and persistence" |
| No dependency info | "Step 3 — *depends on step 2*" or "*parallel with step 1*" |

---

## Quick-start: fill this out for a new plan

```
## Plan: {Title}

**TL;DR** — {One sentence on what and why.}

**Status:** 🔴 Not started · **Branch:** `feature/{name}` · **Date:** {today}

### Phase 1: {Name} *(blocks nothing)*
- [ ] 1. {Step}
- [ ] 2. {Step} — *parallel with 1*

**Relevant files:**
- `full/path/to/file.cs` — {what to change}

**Verification:**
- [ ] `dotnet build` — 0 warnings
- [ ] `dotnet test` — all pass

### Decisions
| Question | Decision | Rationale |
|---|---|---|
| {Q} | {A} | {Why} |

**Scope:** In: {…}. Out: {…} (because {…}).

### Further Considerations
1. {Question or risk} — Recommended: {…}
```

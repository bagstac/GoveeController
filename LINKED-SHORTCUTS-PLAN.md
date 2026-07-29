# Feature plan: Linked shortcuts

Implementation plan for chaining shortcuts so that applying one runs a sequence of up to three.
Written for another agent to execute. Everything below was derived from reading the current code
(`Shortcut`, `ShortcutService`, `ShortcutRepository`, `AppDbContext`, `Shortcuts.razor`) and from
clarifying questions already answered by the repo owner — **the decisions in section 2 are settled;
do not relitigate them.**

## 1. Goal

- A shortcut can name another shortcut to run *after* it, with an optional delay in between.
- Chains are capped at **3 shortcuts** total (2 links).
- You can set the link while creating a shortcut, and you can link two shortcuts that already exist.
- Applying a shortcut runs it and then continues down its chain.

## 2. Decisions already made (settled — implement as stated)

| Question | Decision |
|---|---|
| Delay between steps? | **Configurable per link.** Each shortcut stores the delay to wait before running its own next step. Chains run inline (apply blocks until the chain finishes), so the delay is capped — see §5. |
| "Add a shortcut while creating one" | **Pick an existing saved shortcut** from a dropdown. There is no inline "define a whole second shortcut in the same form" flow. This is the same mechanism used to link two existing shortcuts, so there is exactly one concept to build. |
| A step fails (offline bulb, etc.) | **Continue to the next step.** Matches the existing per-target best-effort behavior in `ApplyShortcutAsync`. Collect failures from every step and report them together at the end. |
| Do linked shortcuts still show in the list? | **Yes — flat list.** Every shortcut keeps its own row and stays independently applyable; the link is shown as metadata on the row. No nesting/grouping. |

## 3. Design decisions made in this plan

These are choices this plan is making on the implementer's behalf, with rationale. They are
defensible defaults, not requirements from the owner — flag to the owner if you disagree, but do not
silently deviate.

### 3.1 Link direction: a nullable self-referencing FK on `Shortcut`

Add `NextShortcutId` (nullable FK to `Shortcuts.Id`) rather than introducing a separate `Chain`
entity with ordered steps. Reasons:

- It matches the requested mental model directly ("add a shortcut to it that runs after").
- "Link two existing shortcuts" becomes a single field edit rather than creating a container object.
- Followers remain ordinary rows in `Shortcuts`, which is exactly what the flat-list decision needs.

A separate `Chain` entity would be the better model if chains later need names, more than a handful
of steps, or reordering UI. Note that as a possible future migration path; it is not needed now.

### 3.2 A shortcut may be the follower of at most one shortcut

Enforce a **unique index on `NextShortcutId`** (see §4). This is the single most important structural
decision in the plan, because it makes the graph a set of disjoint simple paths instead of a general
functional graph.

Why it matters: without it, both `A → B` and `C → B` are legal, and then "how long is B's chain?"
has no single answer — validating the 3-shortcut cap would require walking a *branching* tree of
predecessors, and the cap itself would become ambiguous (is `A→B→D` plus `C→B→D` two chains of 3, or
one structure of 4?). With the constraint, every node has at most one predecessor and at most one
successor, so `upstream + downstream` arithmetic is exact and the validation in §6 is a short
finite walk.

Cost: you cannot reuse one shortcut as the tail of two different chains. Given the 3-shortcut cap
and that this is a personal home-automation app, that is an acceptable trade. Surface it as a clear
validation message ("That shortcut already runs after another shortcut"), not a silent failure.

Note for SQLite specifically: a plain `.IsUnique()` index on a nullable column is sufficient —
SQLite treats `NULL`s as distinct in unique indexes, so any number of unlinked shortcuts coexist
without needing a filtered index.

### 3.3 Applying a shortcut runs from that shortcut to the end of its chain

Given `A → B → C`: applying `A` runs A, B, C; applying `B` runs B, C; applying `C` runs only C.
This is consistent with the flat-list decision (every row is applyable) and with the row metadata
(`B`'s row will visibly say it runs `C` next, so running B→C is what the UI already promises). There
is deliberately **no** "run only this one, ignore the chain" affordance — add one later only if asked.

### 3.4 No new `IShortcutRepository` methods

Chain resolution and validation both need predecessor lookups and path walks. Rather than adding
`GetPredecessorAsync`/`GetChainAsync` to the repository interface (and updating every Moq setup),
have `ShortcutService` call the existing `GetAllAsync()` **once** per operation and resolve the graph
in memory. The dataset is tens of rows at most; this keeps `IShortcutRepository` untouched and makes
the new logic trivially unit-testable with a single stubbed call.

Do not add `LinkShortcutsAsync` to `IShortcutService` either — `UpdateShortcutAsync` already carries
the whole shortcut and the UI edits via the existing form, so a second write path would only be
another thing to keep consistent.

### 3.5 Delay is testable via `TimeProvider`

Inject `TimeProvider` into `ShortcutService` (default `TimeProvider.System` in DI registration) and
use `timeProvider.Delay(...)` instead of `Task.Delay(...)`. This keeps chain-ordering and
delay-honored tests instant instead of sleeping for real. `TimeProvider` is built into .NET 8+, so
this adds no production dependency; tests need `Microsoft.Extensions.TimeProvider.Testing` for
`FakeTimeProvider`.

If you'd rather avoid the test package, the fallback is to test chains with `delay: 0` and cover the
delay value only through validation/persistence assertions — acceptable but weaker.

## 4. Data model changes

### `src/GoveeController.Domain/Shortcuts/Shortcut.cs`

Add two properties (with XML doc comments matching the file's existing style):

```csharp
/// <summary>
/// Id of the shortcut to run after this one, or null if this shortcut ends its chain. A shortcut
/// may be the follower of at most one other shortcut (enforced by a unique index), so chains are
/// simple linear paths of at most 3 shortcuts. Deliberately a plain FK with no navigation
/// property — the UI resolves names from the already-loaded shortcut list, and the service walks
/// chains in memory (see LINKED-SHORTCUTS-PLAN.md §3.4).
/// </summary>
public int? NextShortcutId { get; set; }

/// <summary>
/// Seconds to wait after applying this shortcut before applying <see cref="NextShortcutId"/>.
/// Ignored (and meaningless) when <see cref="NextShortcutId"/> is null. 0 means run immediately.
/// </summary>
public int NextShortcutDelaySeconds { get; set; }
```

`NextShortcutDelaySeconds` is non-nullable with a default of 0 on purpose: making it `int?` would
create a two-field invariant ("null iff NextShortcutId is null") that nothing benefits from
enforcing.

### `src/GoveeController.Infrastructure/Persistence/AppDbContext.cs`

Inside the existing `modelBuilder.Entity<Shortcut>(...)` block:

```csharp
// Self-referencing optional link to the next shortcut in a chain. SetNull (not Cascade) so that
// deleting a followed shortcut just breaks the link instead of deleting its predecessor too.
entity.HasOne<Shortcut>()
    .WithMany()
    .HasForeignKey(s => s.NextShortcutId)
    .OnDelete(DeleteBehavior.SetNull);

// At most one shortcut may point at any given follower — this is what keeps chains linear and
// makes the 3-shortcut cap unambiguous. SQLite treats NULLs as distinct, so unlinked shortcuts
// are unaffected.
entity.HasIndex(s => s.NextShortcutId).IsUnique();

entity.Property(s => s.NextShortcutDelaySeconds).HasDefaultValue(0);
```

### Migration

Generate it — do not hand-write it:

```bash
dotnet ef migrations add AddLinkedShortcuts --project src/GoveeController.Infrastructure --startup-project src/GoveeController.Web --output-dir Persistence/Migrations
```

Then **read the generated migration** and confirm it (a) adds both columns, (b) creates the unique
index, and (c) uses `ReferentialAction.SetNull`. SQLite migrations that alter tables sometimes get
rebuilt as table-copy operations; verify the existing `ShortcutTarget` FK survives. Existing rows get
`NextShortcutId = NULL` and `NextShortcutDelaySeconds = 0`, so the migration is safe on live data
(the Pi's `./data/shortcuts.db` and the local Docker one both have real shortcuts in them).

Migrations run automatically at startup (`Program.cs`), so no manual step on deploy.

## 5. Delay bounds

- Valid range: **0–60 seconds** per link.
- Rationale for the cap: chains run inline inside the Blazor Server call, so a 3-shortcut chain with
  two maxed links already blocks the Apply button for ~2 minutes. Anything longer needs the
  background-runner design that was explicitly *not* chosen.
- `Task.Delay`/`timeProvider.Delay` **must** be passed the `CancellationToken`. `Shortcuts.razor`
  cancels its `_cts` on dispose, so navigating away mid-chain must abort the remaining steps rather
  than continue running against a torn-down component. This is intended behavior — document it in a
  comment so a future reader doesn't "fix" it.

## 6. Validation rules (implement exactly)

Add these to `ShortcutService`, alongside the existing `ValidateShortcutInputs`. All of them operate
on the in-memory list from `GetAllAsync()`.

Definitions, given the §3.2 uniqueness constraint:

- `successor(X)` = the shortcut with `Id == X.NextShortcutId`, or none.
- `predecessor(X)` = the unique shortcut whose `NextShortcutId == X.Id`, or none.
- `upstreamCount(X)` = number of nodes from X's chain head through X, inclusive (walk predecessors).
- `downstreamCount(X)` = number of nodes from X through its chain tail, inclusive (walk successors).

When setting `X.NextShortcutId = Y` (non-null), reject with `ArgumentException` if:

1. **Self-link** — `Y.Id == X.Id`.
   → *"A shortcut cannot run itself."*
2. **Y already has a predecessor** other than X — `predecessor(Y)` exists and is not X.
   → *"That shortcut already runs after another shortcut."*
3. **Cycle** — walking predecessors from X reaches Y.
   → *"That would create a loop."*
4. **Too long** — `upstreamCount(X) + downstreamCount(Y) > 3`.
   → *"A chain can link at most 3 shortcuts."*
5. **Bad delay** — delay `< 0` or `> 60`.
   → *"Delay must be between 0 and 60 seconds."*
6. **Unknown target** — no shortcut with id `Y`. → `KeyNotFoundException`.

Notes:

- Only the *upstream* side needs the cycle check (rule 3); X's existing downstream is being replaced.
- For a **newly created** shortcut, `upstreamCount` is 1 (nothing points at it yet), so rule 4
  reduces to `downstreamCount(Y) ≤ 2`.
- Setting `NextShortcutId = null` (unlinking) needs no validation. Also reset
  `NextShortcutDelaySeconds` to 0 when unlinking, so stale values don't reappear if it's relinked.
- Both predecessor and successor walks should carry a **defensive iteration cap** (4 is plenty).
  Cycles are prevented by construction, but a corrupted database row must not spin forever.
- Validation belongs in the service, not just the UI — the service is the trust boundary (this is
  already the stated convention in `ValidateShortcutInputs`'s comment).

## 7. Application layer changes

### `IShortcutService` / `ShortcutService`

**Signature changes** — add two parameters to both create and update:

```csharp
Task<Shortcut> CreateShortcutAsync(
    string name,
    IReadOnlyList<(string Sku, string DeviceId)> targets,
    bool powerOn,
    int? brightness,
    RgbColor? color,
    int? colorTemperatureKelvin,
    int? nextShortcutId,                 // new
    int nextShortcutDelaySeconds,        // new
    CancellationToken cancellationToken = default);
```

…and the same two on `UpdateShortcutAsync`. Update the XML docs to mention the chain rules and point
at this file.

**New method** for populating the UI dropdown, so the eligibility rules live in exactly one place:

```csharp
/// <summary>
/// Lists shortcuts that may legally be set as the next step for <paramref name="forShortcutId"/>
/// (pass null when creating a brand-new shortcut). Applies the same rules as
/// CreateShortcutAsync/UpdateShortcutAsync so the UI can offer only valid choices, but the
/// service still validates on write — this is a convenience, not the enforcement point.
/// </summary>
Task<IReadOnlyList<Shortcut>> ListEligibleNextShortcutsAsync(
    int? forShortcutId, CancellationToken cancellationToken = default);
```

**`ApplyShortcutAsync` becomes a chain walk.** Restructure as:

- Load all shortcuts once via `GetAllAsync()`.
- Resolve the ordered chain starting at `id` (max 3 nodes, defensive cap).
- For each step, in order:
  - Apply it to every target using the **existing** per-target best-effort loop (extract the current
    body of `ApplyShortcutAsync` into a private `ApplyOneAsync(Shortcut, List<failures>, ct)` —
    do not duplicate the logic).
  - If the step has a successor and a non-zero delay, `await timeProvider.Delay(...)` with the token
    **before** the next step.
- After all steps, if any failures were collected, throw `ShortcutApplyException` with totals
  aggregated across the whole chain.
- Keep `KeyNotFoundException` for an unknown starting id.

Preserve the existing comment explaining *why* application is best-effort per target — it documents a
real device behavior (bulbs routinely offline) and is still true per step.

### `ShortcutApplyException.cs`

Failures now come from potentially different shortcuts, so a bare device id is no longer enough to
tell the user where something went wrong. Extend the failure record:

```csharp
public sealed record ShortcutTargetFailure(
    string DeviceSku,
    string DeviceId,
    string ErrorMessage,
    int ShortcutId,
    string ShortcutName);
```

Add the two new members as **required positional parameters, not optional defaults** — there is only
one construction site in production code (`ShortcutService`), and making them optional would let a
future caller silently omit the attribution that is the whole point of the change. Update
`BuildMessage` to include the step name, and update the affected assertions in
`tests/GoveeController.Application.Tests/Shortcuts/ShortcutServiceTests.cs`.

`SucceededCount`/`TotalCount` keep counting **targets**, now summed across all steps of the chain.
Document that explicitly in the XML doc, because "2 of 5" for a chain is otherwise ambiguous.

### DI registration

`ShortcutService` gains a `TimeProvider` constructor parameter. It is registered at
`src/GoveeController.Infrastructure/ServiceCollectionExtensions.cs:78`
(`services.AddScoped<IShortcutService, ShortcutService>();`) — add `services.AddSingleton(TimeProvider.System);`
alongside it. (`TryAddSingleton` also works but needs
`using Microsoft.Extensions.DependencyInjection.Extensions;`.)

## 8. Web / UI changes — `Components/Pages/Shortcuts.razor`

### 8.1 Form fields (New shortcut / Edit shortcut)

Add two cells to the existing `.form-grid`, after the warmth dropdown:

- **"Then run (optional)"** — `<select>` bound to `_form.NextShortcutId`, first option `"None"`
  (empty value), populated from `ListEligibleNextShortcutsAsync`. Follow the same
  `@onchange`-with-a-handler pattern the warmth dropdown now uses (parse empty string → `null`).
- **"Delay before next (seconds)"** — number input bound to `_form.NextShortcutDelaySeconds`,
  `min="0" max="60"`, with a `<div class="control-hint">Enter a whole number from 0 to 60.</div>`
  beneath it. This matches the brightness convention just established across the app. Only render
  (or disable) this cell when a next shortcut is selected — a delay with nothing to delay is noise.

The form grid already collapses to a single column under 600px, so both new fields lay out correctly
on mobile with no extra CSS.

`ShortcutFormModel` gains `public int? NextShortcutId { get; set; }` and
`public int NextShortcutDelaySeconds { get; set; }`. Add both to `ResetForm()` (null / 0) and to
`StartEdit()` (copy from the shortcut) — **both**, or editing will leak the previous shortcut's link
into the next edit.

Refresh the eligible-next list after every successful save/delete, since linking changes what's
eligible.

### 8.2 Row metadata

In the existing `.meta` div, append the link when present — resolve the name from the already-loaded
`_shortcuts` list (same approach as `DeviceNamesFor`):

```
Family Room R2 L · On · 42% · → then Movie Mode (after 10s)
```

Omit the `(after Ns)` parenthetical when the delay is 0. Optional polish (do it only if it stays
simple): on a chain *head* — a shortcut with a successor and no predecessor — render the whole chain
(`→ Dim → Off`) so a 3-step chain is legible at a glance.

### 8.3 Linking two existing shortcuts

This needs **no new UI**: edit shortcut A, set "Then run" to B, save. Confirm this works end to end
before considering the feature done, since it is an explicit requirement. Unlinking is the same flow
with "None". Do not add a separate "Link"/"Unlink" button unless the owner asks.

### 8.4 Error surface

`ArgumentException` messages already flow through `UserFacingError.From` and render in the error
banner verbatim, so the validation strings in §6 reach the user as written. Verify the existing
`ShortcutApplyException` catch block still reads sensibly for a chain — it currently produces
"Applied to N of M device(s). Failed: …"; consider grouping the failure names by step now that
failures carry `ShortcutName`.

## 9. Tests to add — `tests/GoveeController.Application.Tests/Shortcuts/ShortcutServiceTests.cs`

Follow the existing file's style (xUnit + Moq, `new Mock<IShortcutRepository>()`,
`new Mock<IDeviceControlService>()`).

Validation:

- Self-link rejected.
- Linking to a shortcut that already has a predecessor rejected.
- Cycle rejected (`A→B` exists; setting `B→A` throws).
- 4-chain rejected (`A→B→C` exists; setting a link that would make it 4 throws).
- 3-chain accepted (the boundary case — assert it does **not** throw).
- Delay `-1` and `61` rejected; `0` and `60` accepted.
- Unknown `nextShortcutId` throws `KeyNotFoundException`.

Apply:

- Chain runs every step **in order** (assert call ordering on the `IDeviceControlService` mock, e.g.
  via `MockSequence` or a recorded call list).
- Delay is awaited between steps and not after the last one (`FakeTimeProvider`).
- A failing step does **not** stop later steps, and the thrown `ShortcutApplyException` aggregates
  failures from all steps with correct `ShortcutName` attribution.
- Cancelling the token mid-chain stops it.
- Applying a mid-chain shortcut runs only from there onward.

Repository — `tests/GoveeController.Application.Tests/Persistence/ShortcutRepositoryTests.cs`
(this file already uses real in-memory SQLite, so FK behavior is genuinely exercised):

- Deleting a followed shortcut sets the predecessor's `NextShortcutId` to null rather than deleting
  it or throwing.
- The unique index rejects a second shortcut pointing at the same follower.
- `UpdateAsync` round-trips `NextShortcutId` and `NextShortcutDelaySeconds`.

`ShortcutRepository.UpdateAsync` currently copies fields explicitly — **add the two new fields to
that copy block.** Omitting them is the single most likely silent bug in this whole feature: the
create path would work, the edit path would appear to work, and the link would just never persist.

## 10. Known constraint worth surfacing: Govee's rate limit

Do the arithmetic before assuming chains "just work" on a real account:

Applying one shortcut to 6 bulbs with power + brightness + colour issues ~3 calls per bulb = **18
API calls**. A 3-step chain over the same 6 bulbs is **~54 calls**. Govee's documented ceiling is
**30 requests/minute**, and this app's own client-side limiter
(`ServiceCollectionExtensions`: `SlidingWindowRateLimiter`, 25 permits/minute, queue limit 5) will
start rejecting well before that.

So a 3-step chain across many devices **cannot** complete inside one minute, no matter what the code
does. This is a physical constraint of the API, not a bug to fix. Handle it honestly:

- The per-link delay is genuinely load-bearing here: a 60s delay puts each step's burst in a
  different rate-limit window. Say so in the delay field's hint text or the page's help text.
- Rejections already surface as a friendly message ("Govee rate limit reached; please wait a
  moment and try again") via `TranslateRateLimitRejection`, and the continue-on-failure decision
  means a throttled step won't kill the rest of the chain — but the result will be a partially
  applied chain, which the user needs to understand is expected under load.
- **Do not** raise the client-side limiter above Govee's own 30/min to make this "work" — that just
  moves the rejection server-side and risks the account's key.

If the owner wants large multi-device chains to run reliably, that is the background-runner design
(explicitly deferred in §2) plus pacing, and should be raised as a follow-up rather than smuggled in.

## 11. Verification

Follow the pattern this repo has used throughout — unit tests are necessary but not sufficient;
verify live against the local Docker deployment and real bulbs.

1. `dotnet build` — expect 0 warnings, 0 errors (`TreatWarningsAsErrors` is on in CI).
2. `dotnet test` — all existing tests plus the new ones pass.
3. `docker compose up --build -d`, wait for healthy, then in the browser:
   - Create shortcut A with "Then run" = an existing B and a 5s delay. Confirm A's row shows
     `→ then B (after 5s)`.
   - Apply A. Confirm B's settings land on the bulbs ~5s after A's, and that B is not applied twice.
   - Edit an existing C, set "Then run" = an existing D, save, reopen the edit form and confirm the
     link and delay reloaded correctly (this is the "link after creation" requirement, and reopening
     is what catches a missing `StartEdit` copy).
   - Try to create a 4-long chain and confirm the validation message appears in the error banner.
   - Try to link a shortcut that already follows another and confirm the message.
   - Delete a followed shortcut and confirm its predecessor survives with the link cleared (not a
     crash, not a cascade delete).
   - Check at 375px width that both new fields stack in the single-column form.
4. `docker logs` — confirm no unhandled exceptions, and check whether the chain tripped the rate
   limiter (expected on multi-bulb chains; see §10).

## 12. Explicitly out of scope

Do not build these as part of this feature:

- Background/asynchronous chain execution, progress reporting, or delays longer than 60s (§2, §10).
- Defining brand-new shortcuts inline while creating one (§2 — dropdown of existing only).
- Chains longer than 3, branching, or conditional steps.
- Reusing one shortcut as the follower of multiple chains (§3.2).
- A separate named `Chain` entity or reordering UI (§3.1 notes it as the future path if needed).
- Scheduling ("run this chain at 7am") — unrelated feature, do not conflate.

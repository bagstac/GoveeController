# Plan: scheduled shortcuts

Adds the ability to schedule a shortcut to run automatically — either on a recurring
days-of-week + time pattern, or once at a specific date and time. Touches every layer
(new entity, new service, new repository, a background runner, a new page) but requires
**no new processes or containers**: the runner is a hosted `BackgroundService` inside the
existing web app, and schedules are stored in the existing SQLite database. Written for
another agent to execute.

## 1. Goal

A new **Schedules** page (third nav item, after Devices and Shortcuts) where the user can:

- Create a schedule: pick a shortcut, then either
  - **Recurring**: a time of day + any combination of Mon–Sun checkboxes, or
  - **One-time**: a specific date + time.
- See every schedule listed with the shortcut name, a human description of when it runs
  ("Mon, Wed, Fri at 10:00 PM", "Once on Aug 2 at 7:00 AM"), its next run time, and an
  enabled/disabled toggle.
- Edit and delete schedules.

A background runner inside the app fires each schedule at its due time by calling the
existing `IShortcutService.ApplyShortcutAsync` — so a scheduled shortcut that chains to
other shortcuts runs its whole chain, exactly like clicking Apply.

## 2. Decisions already made (settled — implement as stated)

Answered directly by the repo owner. Do not relitigate.

| Question | Decision |
|---|---|
| Recurrence model | **Days-of-week checkboxes + a time of day** (alarm-clock style), plus a separate one-time date+time mode. No cron expressions. |
| Missed runs (app down when due) | **Grace window, then skip.** If the app comes back within the grace window (5 minutes) of a missed occurrence, fire it late; beyond that, skip to the next occurrence. |
| UI placement | **New Schedules page** with its own nav link. |
| One-time schedules after firing | **Auto-delete.** The row disappears once it has fired (the log records that it ran). A one-time schedule missed beyond the grace window is also deleted — its moment is gone — with a log line saying it was missed. |

## 3. Design decisions made in this plan

### 3.1 Times are local wall-clock, driven by the container's `TZ`

Schedules store **local** wall-clock values (`TimeOnly` / `DateOnly`), not UTC. "10:00 PM"
must mean 10 PM on the user's wall regardless of DST. The runner converts to UTC at
evaluation time using `TimeProvider.LocalTimeZone` (via `TimeProvider.GetLocalNow()` /
`TimeZoneInfo` conversion), which on Linux follows the `TZ` environment variable.

**The container currently has no `TZ` set, so `LocalTimeZone` is UTC in Docker today.**
The deployment must set it (§4.6). Do not build a per-schedule or in-app timezone picker —
one household, one timezone, one env var.

DST consequences (accept and document in code comments, do not engineer around):

- Spring-forward: a time inside the skipped hour (e.g. 2:30 AM) doesn't exist that day.
  `TimeZoneInfo.IsInvalidTime` — map it forward to the post-transition instant.
- Fall-back: an ambiguous time occurs twice; fire only the first occurrence
  (`TimeZoneInfo` maps ambiguous times deterministically — that's fine).

### 3.2 One `Schedule` entity for both modes, discriminated by `DaysOfWeekMask`

One table, not two. `DaysOfWeekMask == 0` means one-time (`OneTimeDateLocal` must be set);
a non-zero mask means recurring (`OneTimeDateLocal` must be null). `TimeOfDayLocal` is
required in both modes. This keeps the repository, the list UI, and the runner working
with one shape, and the validity rule is a single XOR check in the service.

The mask is a `[Flags]` enum in Domain (`ScheduleDays`) with each bit `1 << (int)DayOfWeek`
so conversion to/from `DateTime.DayOfWeek` is a shift, not a lookup table.

### 3.3 Store the next due instant (`NextRunAtUtc`) instead of recomputing per tick

Each schedule row carries `NextRunAtUtc` (nullable — null while disabled). The runner's
tick is then a trivial query: fire everything with `NextRunAtUtc <= now`, where
`now - NextRunAtUtc <= grace` means "fire", beyond grace means "skip forward".

Why store it rather than derive it each tick: it survives restarts (which is exactly what
makes the grace window work — a reboot at 9:59 PM still knows the 10:00 PM run was due),
it gives the UI its "next run" column for free, and it makes the runner's read path index-
friendly. The cost is that it must be recomputed at every write point: create, edit,
enable, and after each fire/skip. Centralize that in one service method so no write path
can forget.

On **re-enable**, recompute from *now* — a schedule disabled for a week must not instantly
fire its stale past `NextRunAtUtc` on the toggle.

### 3.4 The runner is a `BackgroundService` in Infrastructure; the logic lives in Application

- `ScheduleRunnerService : BackgroundService` (Infrastructure, new `Scheduling/` folder,
  registered via `AddHostedService` in `ServiceCollectionExtensions`). It is a dumb loop:
  tick every 30 seconds (`Task.Delay(TimeSpan, TimeProvider, CancellationToken)` — same
  API `ShortcutService` already uses), create a DI scope, call the Application-layer tick
  method, catch-log-continue.
- `IScheduleService.RunDueSchedulesAsync(CancellationToken)` (Application) holds all the
  real logic: query due rows, fire/skip per §3.3, delete finished one-times, recompute
  `NextRunAtUtc`. This is the unit-testable part — `FakeTimeProvider` (already a test
  dependency) controls both the clock and `LocalTimeZone` (`SetLocalTimeZone`).

Two traps the executing agent must not fall into:

1. **`BackgroundService` is a singleton; `AppDbContext`/repositories/`IShortcutService`
   are scoped.** Inject `IServiceScopeFactory` and create a scope per tick. Injecting the
   scoped services directly fails at startup.
2. **An exception escaping `ExecuteAsync` stops the whole host** (.NET's default
   `BackgroundServiceExceptionBehavior.StopHost`). The loop body must catch everything
   (except `OperationCanceledException` on shutdown), log, and keep ticking. A schedule
   that fails to apply (`ShortcutApplyException` — some bulbs offline) is logged at
   Warning with the per-target detail and is otherwise treated as fired: recompute next /
   delete one-time as normal. There is no UI session to surface errors to.

Startup ordering is already safe: `Program.cs` runs `db.Database.Migrate()` before
`app.Run()`, and hosted services start during `Run()` — the runner can never see an
unmigrated database.

### 3.5 Due schedules run sequentially, not in parallel

If several schedules land on the same tick, run them one after another. Parallel runs
would multiply pressure on the client-side Govee rate limiter (25 req/min shared budget —
see `ServiceCollectionExtensions`). **Do not raise the rate limiter to compensate**; that
only moves the failure to Govee's side and risks the API key. Sequential is also the
simplest thing that can work, and two schedules colliding on the same minute is rare.

The runner passes `CancellationToken` from `ExecuteAsync` straight through to
`ApplyShortcutAsync`, so container shutdown aborts an in-flight chain the same way
navigating away already does in the UI.

### 3.6 Deleting a shortcut deletes its schedules (cascade)

A schedule without its shortcut is meaningless. FK `Schedule.ShortcutId → Shortcut.Id`
with `DeleteBehavior.Cascade`. This means the Shortcuts page's Delete button silently
removes schedules too — acceptable for now; a confirmation dialog is out of scope (§5).

Unlike the linked-shortcuts migration, this one adds a **new table only** — no FK is added
to the existing `Shortcuts` table, so EF/SQLite will emit a plain `CREATE TABLE`, not the
copy/drop/rename rebuild observed last time. If the generated migration touches the
`Shortcuts` table at all, stop and investigate.

## 4. Implementation

### 4.1 Domain — `src/GoveeController.Domain/Schedules/`

`ScheduleDays.cs`:

```csharp
[Flags]
public enum ScheduleDays
{
    None = 0,
    Sunday = 1 << 0,   // 1 << (int)DayOfWeek.Sunday
    Monday = 1 << 1,
    Tuesday = 1 << 2,
    Wednesday = 1 << 3,
    Thursday = 1 << 4,
    Friday = 1 << 5,
    Saturday = 1 << 6
}
```

`Schedule.cs` — entity, XML-doc'd like `Shortcut`:

| Property | Type | Notes |
|---|---|---|
| `Id` | `int` | PK |
| `ShortcutId` | `int` | Required FK; plain FK without navigation property, same style as `Shortcut.NextShortcutId`. |
| `DaysOfWeekMask` | `ScheduleDays` | `None` ⇒ one-time. Stored as int. |
| `OneTimeDateLocal` | `DateOnly?` | Set iff one-time. |
| `TimeOfDayLocal` | `TimeOnly` | Both modes. |
| `IsEnabled` | `bool` | Default true. |
| `NextRunAtUtc` | `DateTime?` | See §3.3. Null while disabled. |
| `CreatedAtUtc` | `DateTime` | Display/ordering, same as `Shortcut`. |

EF Core 8+ maps `DateOnly`/`TimeOnly` to SQLite TEXT natively — no value converters needed.

### 4.2 Application — `src/GoveeController.Application/Schedules/`

- `IScheduleRepository` — `GetAllAsync`, `GetByIdAsync`, `AddAsync`, `UpdateAsync`,
  `DeleteAsync`, mirroring `IShortcutRepository`'s shapes and doc style.
- `IScheduleService` / `ScheduleService`:
  - `ListSchedulesAsync` — all schedules, newest first.
  - `CreateScheduleAsync(shortcutId, daysOfWeek, oneTimeDateLocal, timeOfDayLocal, isEnabled, ct)`
  - `UpdateScheduleAsync(id, …same…, ct)`
  - `SetEnabledAsync(id, bool, ct)` — recompute-from-now on enable (§3.3), null out on disable.
  - `DeleteScheduleAsync(id, ct)`
  - `RunDueSchedulesAsync(ct)` — the tick body (§3.4).
  - Validation (throw `ArgumentException`, matching `ShortcutService`'s style):
    - shortcut must exist (`KeyNotFoundException` if not);
    - exactly one of `daysOfWeek != None` / `oneTimeDateLocal != null` (the XOR from §3.2);
    - a one-time schedule's date+time must be in the future — compare against
      `_timeProvider.GetLocalNow()`, **never** `DateTime.Now`.
- `NextOccurrence.cs` — a pure static calculator, the heart of the feature and the most
  test-worthy code in it:

  ```csharp
  public static DateTime? ComputeNextRunAtUtc(
      ScheduleDays days, DateOnly? oneTimeDateLocal, TimeOnly timeOfDayLocal,
      DateTimeOffset nowUtc, TimeZoneInfo localTimeZone)
  ```

  Recurring: scan forward from local-now, at most 8 days, for the first enabled day whose
  time hasn't passed; convert with `TimeZoneInfo.ConvertTimeToUtc`, handling
  `IsInvalidTime` per §3.1. One-time: the single instant, or null if it's already past.
  Returns null ⇒ nothing left to run (only possible for one-time).

`ScheduleService` takes `IScheduleRepository`, `IShortcutService` (for
`ApplyShortcutAsync` — reuse the chain/failure semantics, do not re-implement), and
`TimeProvider`. Grace window: `private static readonly TimeSpan GraceWindow =
TimeSpan.FromMinutes(5);`.

`RunDueSchedulesAsync` per due schedule (enabled, `NextRunAtUtc <= now`):

1. Overdue beyond grace? Recurring: recompute forward, save, log skip. One-time: delete,
   log missed.
2. Within grace: `ApplyShortcutAsync`; catch `ShortcutApplyException` → log Warning,
   continue. Then one-time → delete; recurring → recompute forward from *after* this
   occurrence, save.

### 4.3 Infrastructure

- `Persistence/ScheduleRepository.cs` — EF implementation, mirrors `ShortcutRepository`.
- `AppDbContext` — add `DbSet<Schedule>`; model config: FK to `Shortcut` with
  `DeleteBehavior.Cascade`, `DaysOfWeekMask` stored as int, index on `NextRunAtUtc`.
- `Scheduling/ScheduleRunnerService.cs` — the `BackgroundService` from §3.4. Infrastructure
  will need the `Microsoft.Extensions.Hosting.Abstractions` package reference if it isn't
  already transitively available (verify before adding).
- `ServiceCollectionExtensions` — register `IScheduleRepository`, `IScheduleService`
  (scoped), `AddHostedService<ScheduleRunnerService>()`.
- Migration: `dotnet ef migrations add AddSchedules` (same project/startup-project flags
  as previous migrations — see git history for the exact command). Verify it's a plain
  `CreateTable` per §3.6.

### 4.4 Web — `Components/Pages/Schedules.razor` + nav

- Nav: add `<NavLink href="schedules">Schedules</NavLink>` in `MainLayout.razor` after Shortcuts.
- Page follows `Shortcuts.razor`'s existing patterns exactly: `@implements IDisposable`
  with a `CancellationTokenSource`, `_busy` flag, `_error` + `UserFacingError.From`,
  `EditForm` + `form-grid`, edit-in-place via `_editingId`.
- List: one row per schedule — shortcut name (resolve from loaded shortcut list, fall back
  to `#id`), description ("Mon, Wed, Fri at 10:00 PM" — collapse to "Every day",
  "Weekdays", "Weekends" when the mask matches; "Once on Aug 2 at 7:00 AM"), next run
  (`NextRunAtUtc` converted to local, or "—" when disabled), an enable/disable toggle,
  Edit / Delete buttons.
- Form: shortcut `<select>`; a Recurring/One-time mode choice; recurring → seven day
  checkboxes + time input; one-time → date input + time input. Use
  `InputDate<DateOnly>` (`Type="InputDateType.Date"`) and `InputDate<TimeOnly>`
  (`Type="InputDateType.Time"`) — both supported since .NET 8.
- Times shown in the UI are already local (that's what's stored); only `NextRunAtUtc`
  needs converting for display.
- CSS: reuse `.shortcut-list` / `.shortcut-row` / `.btn` classes where they fit; add
  schedule-specific classes only if genuinely needed, in the existing style
  (`#888` family, rem spacing).

### 4.5 The Schedules page does not need to know about the runner

No SignalR push, no live refresh when a schedule fires in the background. The page shows
state as of load; a manual reload shows updated next-run times. Live updating is out of
scope (§5).

### 4.6 Docker / configuration — `TZ` (already done — commit `c33a166`)

The `TZ` wiring shipped ahead of this plan: `docker-compose.yml` passes `TZ` through from
`.env` (defaulting to UTC), `.env.example` documents it, and the README has a "Changing
the container's timezone" section. tzdata was verified present in the runtime image
empirically (`TZ=America/Chicago date` inside the container produced CDT), so no
Dockerfile change is needed. **Do not redo any of that.**

What remains for the executing agent: a README section documenting the scheduling
*feature* itself — what it does, the grace-window semantics, one-time auto-delete
behavior, and a pointer to the existing `TZ` section for why the timezone must be set
before creating schedules.

### 4.7 Tests — `tests/…/Schedules/`

All against `FakeTimeProvider` (`Microsoft.Extensions.TimeProvider.Testing`, already
referenced) using `SetUtcNow` + `SetLocalTimeZone` with a DST-observing zone
(e.g. `America/Chicago`) — never the machine's real clock or zone.

- `NextOccurrenceTests`: same-day future time; same-day past time rolls to next enabled
  day; week wraparound; every-day mask; single-day mask; one-time future; one-time past ⇒
  null; spring-forward invalid time maps forward; fall-back ambiguous time fires once.
- `ScheduleServiceTests`: validation matrix (XOR rule, unknown shortcut, past one-time);
  `SetEnabledAsync` recompute-on-enable / null-on-disable; `RunDueSchedulesAsync` — due
  within grace fires (verify `IShortcutService.ApplyShortcutAsync` called with right id),
  beyond grace skips forward without firing, one-time deletes after firing, one-time
  missed beyond grace deletes without firing, `ShortcutApplyException` is swallowed and
  the schedule still advances, disabled schedules never fire.
- Repository round-trip test against in-memory SQLite, mirroring the existing
  `ShortcutRepository` test approach, including cascade delete from shortcut → schedule.

Current suite is 76 tests; the count will grow. All must pass; none of the existing 76
may change result.

## 5. Out of scope

- Timezone picker in the UI, or per-schedule timezones.
- Run-history table ("last ran at…", success/failure log in the UI) — `docker logs` is the record.
- Live-refreshing the Schedules page when the runner fires.
- Confirmation dialog when deleting a shortcut that has schedules.
- Sunrise/sunset or interval ("every N hours") triggers.
- Any change to the rate limiter (§3.5), `ShortcutService`'s apply/chain semantics, or the Devices page.
- Multi-instance coordination/distributed locking — this app is a single container by design.

## 6. Verification

Component-test coverage for pages is still deferred (`IMPROVEMENT-PLAN.md` §4.5), so
build + unit tests + live checks are the verification.

1. `dotnet build` — 0 warnings, 0 errors (`TreatWarningsAsErrors`).
2. `dotnet test` — all passing; existing 76 unchanged.
3. Inspect the generated migration — new table only (§3.6).
4. Set `TZ` in `.env` to the local zone, `docker compose up --build -d`, wait healthy:
   - `docker exec <container> date` shows local time.
   - `docker logs`: migration applied, runner started, no exceptions.
5. Live at `http://localhost:8080/schedules`:
   - Create a recurring schedule for ~2–3 minutes from now (today's weekday checked).
     Watch the bulbs change at the right wall-clock minute; logs show the run; the row's
     next-run advances to next week (or next enabled day).
   - Create a one-time schedule ~2 minutes out; after it fires, the row is **gone** and
     the bulbs changed.
   - Toggle a schedule disabled → next run shows "—"; re-enable → next run is recomputed
     from now.
   - Schedule a shortcut that chains (`Test off work` → `Test work`) and verify the whole
     chain runs on schedule.
   - Edit and Delete work; validation errors (no days checked, past one-time date) surface
     in the error banner.
   - Restart test: create a schedule due in ~2 min, `docker compose restart` before it
     fires, confirm it still fires on time (restart is far shorter than the grace window).
6. Check `docker logs` end-to-end for unhandled exceptions.

`.env` holds a real API key controlling six real bulbs; scheduled runs will visibly change
them. That's expected — it's how every feature here is validated.

## 7. Repo context you'll need

- Branch off `main` (`6711953` at time of writing); feature branch, do not merge or push
  without being asked.
- Conventions: strict Clean Architecture (`Web → Application → Domain`, Infrastructure
  implements Application interfaces, wired only in `ServiceCollectionExtensions` /
  `Program.cs`); XML docs on all public members; inline comments say *why*, not *what*;
  detailed multi-paragraph commit messages ending
  `Co-Authored-By: Claude <noreply@anthropic.com>`.
- `TimeProvider` is already registered (`services.AddSingleton(TimeProvider.System)`) and
  already used by `ShortcutService` for chain delays — reuse it; never call
  `DateTime.Now`/`UtcNow` directly in new code.
- Delete this file (`SCHEDULED-SHORTCUTS-PLAN.md`) in the final commit; git history
  preserves it.

# GoveeController — Code Review & Improvement Plan

> [!NOTE]
> **Status: implemented, except for the items deliberately left undone.** Kept as a record of the
> review reasoning and of what was consciously deferred.
>
> Everything in priorities 1, 3, 4 (except 4.5), and 5 is done and merged. Deliberately **not**
> done, and not oversights:
>
> - **§2.2** (reverse-proxy / basic-auth deployment) — optional. The chosen posture is documented in
>   the README's "Security" section instead: keep it on a trusted LAN, or put a proxy/VPN in front.
> - **§2.5** (Data Protection keys unencrypted at rest) — accepted risk for a single-user home
>   deployment; the keys are persisted to the bind-mounted `./data/keys` so sessions survive restarts.
> - **§4.5** (bUnit component tests) — optional and larger than it's worth right now.
>
> Note the "21/21 tests passing" figure below is from the original review; the suite is now 76.

Review date: 2026-07-26 · Reviewed at commit `182cbfb` · Reviewer: Claude (Opus 5)

This plan is written to be executed by another agent. Items are ordered by priority.
Each item states **what**, **where**, **why**, and **how to verify**. Items are
independent unless a dependency is called out, so they can be tackled in separate
commits/PRs.

**Ground rules for the executing agent:**

- Run `dotnet build` and `dotnet test` after each item. Both must stay clean
  (currently: 0 warnings, 21/21 tests passing).
- This app controls real hardware. For anything touching command dispatch, verify
  against the running container (`docker compose up --build -d`, then
  <http://localhost:8080>) — not just tests.
- Do **not** commit `.env` or any real API key. `.gitignore` already covers it;
  keep it that way.
- Preserve the existing comment style: comments explain *why*, not *what*.

---

## Priority 1 — Correctness bugs (confirmed by code reading)

### 1.1 Applying a shortcut aborts on the first failing device

**Where:** `src/GoveeController.Application/Shortcuts/ShortcutService.cs` →
`ApplyShortcutAsync` (the `foreach` at ~line 100).

**Problem:** Targets are applied in a sequential `foreach` with no per-target error
handling. `GoveeApiClient` throws `GoveeApiException` when Govee reports a
body-level failure — most commonly `"Device is offline. Please check the Wi-Fi
connection."`, which happens routinely with these bulbs. The exception propagates
out of the loop, so **every remaining target is silently skipped**.

Real-world impact: an "All Off" shortcut covering 6 bulbs, where bulb #2 is
offline, leaves bulbs #3–#6 on. The user sees one error message and reasonably
assumes the whole thing failed, when it partially succeeded.

Note this is *inconsistent with the bulk controls on the Devices page*, which
already isolate failures correctly (each `DeviceCard` catches its own error in
`RunCommandAsync`). Shortcuts should behave the same way.

**Fix:** Continue through all targets, collecting failures, then surface an
aggregate result. Suggested shape:

- Wrap each `ApplyToTargetAsync` call in a try/catch.
- Collect `(target, exception)` pairs.
- If any failed, throw an `AggregateException` (or a new
  `ShortcutPartiallyAppliedException` carrying the per-device failures) *after*
  the loop completes.
- Update `Shortcuts.razor` → `ApplyAsync` to render a message that distinguishes
  "all failed" from "applied to N of M devices; these failed: …".

**Verify:** Add a unit test: 3 targets, mock `IDeviceControlService` to throw for
the middle one, assert `TurnOffAsync` was called for targets 1 *and* 3, and that
the failure is still reported.

---

### 1.2 Device state parsing throws on unexpected JSON shapes

**Where:** `src/GoveeController.Infrastructure/Govee/GoveeApiClient.cs` → `MapState`
(4 call sites: lines ~205, 208, 211, 218).

**Problem:** `capability.State?.Value.GetInt32()` is called unconditionally on a
`JsonElement`. `GetInt32()` throws `InvalidOperationException` if the element is
anything other than a number — a string (`"100"`), an object, or `null`. Govee's
API is known to be inconsistent in exactly this way: capability `value` shapes
already vary by type (int for brightness, nested object for scenes), and this
codebase has already been bitten once by Govee returning unexpected response
shapes (the HTTP-200-with-error-body issue, see `ThrowIfGoveeError`).

If Govee ever returns a string or object for one of these instances, the entire
device card fails to load with a confusing `InvalidOperationException` rather
than degrading gracefully on the one field it couldn't parse.

**Fix:** Use `TryGetInt32` guarded by a `ValueKind` check, and skip (leave `null`)
rather than throw when a value isn't the expected shape. Suggested helper:

```csharp
private static int? TryReadInt(StateValueDto? state) =>
    state is { Value.ValueKind: JsonValueKind.Number } v && v.Value.TryGetInt32(out var i)
        ? i
        : null;
```

Then `powerOn = TryReadInt(capability.State) == 1;` etc.

**Verify:** See item 4.1 — this code path currently has **zero test coverage**
(the only state test covers the HTTP-error path). Add tests covering a
well-formed response, a string-valued field, and a missing `state` object.

---

## Priority 2 — Security

Context: this app has **no authentication by design** (a deliberate, documented
choice — see README "What this app does not do"). That is defensible for a trusted
home LAN. The items below are about making the risk explicit and bounding the
blast radius, not about reversing that decision.

### 2.1 Document the exposure risk prominently, and warn against port-forwarding

**Where:** `README.md`.

**Problem:** Anyone who can reach port 8080 can control every light on the
account, with no credential. The current README mentions this only in a
"What this app does not do" bullet at the very bottom. Someone following the
Raspberry Pi setup instructions will not see it before deploying.

Worth being blunt about: if this is port-forwarded or exposed via a tunnel, an
unauthenticated stranger controls the user's home lighting, and the Govee API
key's full device budget is exposed to abuse (rate-limit exhaustion, in effect a
denial-of-service on the user's own lights).

**Fix:** Add a short, clearly-marked note in the Docker and Raspberry Pi setup
sections: this app is unauthenticated; keep it on a trusted LAN; do **not**
port-forward it or expose it to the internet without putting an authenticating
reverse proxy in front of it.

### 2.2 Optionally support a reverse-proxy / basic-auth deployment

**Where:** `README.md`, plus optionally `docker-compose.yml`.

**Fix (documentation-only is acceptable):** Document a recommended pattern for
users who *do* want remote access — e.g. Caddy or nginx with basic auth in front,
or Tailscale/WireGuard so it's never internet-facing at all. Prefer documenting
this over building auth into the app; adding an in-app login is significant scope
and easy to get wrong.

If in-app auth is later desired, that should be its own scoped piece of work, not
folded into this plan.

### 2.3 Add least-privilege `permissions` to the CI workflow

**Where:** `.github/workflows/docker-publish.yml`.

**Problem:** The `build-and-push` job correctly scopes `permissions` to
`contents: read` + `packages: write`, but the `test` job declares none, so it
inherits the repository default (potentially write-all).

**Fix:** Add a top-level `permissions: contents: read` and let `build-and-push`
keep its narrower override. Low effort, meaningful hardening.

### 2.4 Verify the API key can't leak into logs

**Where:** `src/GoveeController.Infrastructure/Govee/GoveeApiClient.cs`,
`appsettings*.json`.

**Problem:** The key is sent as the `Govee-API-Key` default request header.
`HttpClient` logging does not log headers at default levels, so there is no known
leak today — but nothing *enforces* that, and raising log verbosity for debugging
could expose it.

**Fix:** Add a brief comment at the header-assignment site noting the key must
never be logged, and confirm no log statement anywhere interpolates
`GoveeApiOptions.ApiKey`. Optionally add a redacting log scope.

### 2.5 Data Protection keys are stored unencrypted

**Where:** `src/GoveeController.Web/Program.cs` (~line 36).

**Problem:** Startup logs `No XML encryptor configured. Key {…} may be persisted
to storage in unencrypted form.` The keys sit on the mounted `/data` volume.

**Assessment:** For this app the keys only protect antiforgery tokens — there are
no sessions or user secrets to steal, so impact is low. Recommend **documenting
this as accepted** rather than adding key encryption, which on Linux/Docker
requires additional setup for little gain here. Include the reasoning so it isn't
re-litigated later.

---

## Priority 3 — Robustness & operations

### 3.1 Fail fast (and clearly) when the API key is missing

**Where:** `src/GoveeController.Web/Program.cs`,
`src/GoveeController.Infrastructure/Govee/GoveeApiOptions.cs`.

**Problem:** With no `GOVEE_API_KEY`, the app starts normally and every page load
fails with a generic Govee error. The user has to infer the cause. (The current
Devices page error message does hint at it; other paths don't.)

**Fix:** Add options validation:

```csharp
services.AddOptions<GoveeApiOptions>()
    .Bind(configuration.GetSection(GoveeApiOptions.SectionName))
    .Validate(o => !string.IsNullOrWhiteSpace(o.ApiKey),
        "Govee API key is not configured. Set the GOVEE_API_KEY environment variable — see README.")
    .ValidateOnStart();
```

Note this makes a missing key a **startup failure**. That is the right trade-off
for a container (fails immediately and visibly in `docker logs` rather than
looking healthy but being useless), but call it out in the README.

### 3.2 Govee rate limiting is not actively managed

**Where:** `src/GoveeController.Infrastructure/ServiceCollectionExtensions.cs`
(`AddStandardResilienceHandler`), `Devices.razor` (bulk controls + auto-refresh).

**Problem:** Govee allows ~30 requests/minute per account. Current usage can
approach or exceed that:

- Auto-refresh at 30s = 1 device-list + 6 state calls = **14 requests/min** with
  6 bulbs, before any user interaction.
- Each bulk control fires **one request per device** (6), and the three bulk
  controls in sequence = 18 requests in seconds.
- `AddStandardResilienceHandler`'s retry strategy *adds* requests on 429, which
  can deepen the hole rather than dig out of it.

The default standard-resilience rate limiter is a concurrency limiter
(PermitLimit 1000) — effectively no limit for this workload.

**Fix (choose one, prefer the first):**

1. Configure a real rate limiter on the resilience pipeline — a sliding-window
   limiter at ~25 req/min leaves headroom under Govee's 30.
2. Failing that, serialize/queue bulk dispatch with a small delay between
   devices.

Also worth surfacing 429s distinctly in the UI ("Govee rate limit reached, try
again shortly") rather than as a generic failure.

**Verify:** Trigger all three bulk controls rapidly with auto-refresh at 30s and
confirm no 429s appear in `docker logs`.

### 3.3 `UseHttpsRedirection` is dead code in the container

**Where:** `src/GoveeController.Web/Program.cs` (~line 58).

**Problem:** The container only listens on HTTP:8080 (`ASPNETCORE_HTTP_PORTS=8080`,
no HTTPS port configured), so every request logs
`Failed to determine the https port for redirect.` The middleware can never do
anything useful in this deployment, and the warning is log noise that could mask
real issues.

**Fix:** Make it conditional — only call `UseHttpsRedirection()` (and `UseHsts()`)
when an HTTPS port is actually configured. Keep it working for anyone running
locally over HTTPS via `dotnet run`.

### 3.4 Auto-refresh timer disposal can race with an in-flight tick

**Where:** `src/GoveeController.Web/Components/Pages/Devices.razor` → `Dispose`
(~line 251) and `RestartAutoRefreshTimer`.

**Problem:** `System.Threading.Timer.Dispose()` does not wait for a callback
already running. If the user navigates away (or changes the interval) mid-tick,
`OnAutoRefreshTick` can call `InvokeAsync` on a disposing component, producing
`ObjectDisposedException` noise in the logs.

**Fix:** Implement `IAsyncDisposable` and use the `DisposeAsync` overload, and/or
guard the callback with a `CancellationTokenSource` cancelled on disposal.
Low user-visible impact; worth doing for log cleanliness.

### 3.5 Add a container healthcheck and Pi-appropriate resource limits

**Where:** `docker-compose.yml`, `Dockerfile`.

**Problem:** No healthcheck, so `restart: unless-stopped` only recovers from a
crash — not from a hung-but-alive process. No memory limit, which matters on the
target hardware: the user's **Raspberry Pi 2 has 1GB RAM total**.

**Fix:**

- Add a `/health` endpoint (`app.MapHealthChecks("/health")` plus
  `builder.Services.AddHealthChecks()`), then a compose `healthcheck:` hitting it.
- Add conservative `deploy.resources.limits.memory` (e.g. 512M) with a comment
  explaining the Pi 2 constraint.

### 3.6 Make `docker-compose.yml` usable with the published image

**Where:** `docker-compose.yml`.

**Problem:** Compose is hardcoded to `build:` from source. Now that CI publishes
`ghcr.io/bagstac/govee-controller:latest`, Pi users are told (in the README) to
use a bare `docker run` instead of compose, because compose would rebuild locally
— which is exactly what we want to avoid on a Pi 2.

**Fix:** Document (or add) a compose override that pulls the published image
instead of building, so Pi users get compose's ergonomics (healthcheck, restart
policy, named volume) without a local build. E.g. a
`docker-compose.pull.yml` with `image:` and no `build:`.

### 3.7 Validate shortcut brightness / color-temperature ranges

**Where:** `src/GoveeController.Application/Shortcuts/ShortcutService.cs` →
`ValidateShortcutInputs`.

**Problem:** Validation covers "color XOR temperature" and "at least one target",
but not value ranges. A shortcut can be saved with brightness `0` or `500`, or a
nonsensical Kelvin value, and only fails later at the Govee API — per device, at
apply time.

**Fix:** Validate brightness is 1–100 and Kelvin is within a sane band
(2000–9000) at create/update time, with a clear `ArgumentException`. The UI
inputs already constrain this, but the service is the real boundary and shouldn't
trust them.

---

## Priority 4 — Test coverage gaps

Current: 21 tests, all in `tests/GoveeController.Application.Tests`. Notable gaps:

### 4.1 `GoveeApiClient.MapState` — the state-response success path (highest value)

**Why it matters:** This is the most fragile mapping code in the project (see
item 1.2) and is completely untested on the success path. The only existing state
test asserts an HTTP error is thrown.

**Add:** Tests using the existing `FakeHttpMessageHandler` covering a full
well-formed state response (power/brightness/color/colorTemperature all mapped
correctly), a response missing some capabilities, and — after item 1.2 —
non-numeric values degrading to `null` instead of throwing.

### 4.2 `RgbColor` conversions

**Why:** Pure, trivially testable, and used on every color operation. A
round-trip bug here would silently produce wrong colors.

**Add:** `ToPackedInt`/`FromPackedInt` round-trip, `ToHex`/`FromHex` round-trip,
`FromHex` accepting both `#RRGGBB` and `RRGGBB`, and `FromHex` throwing on
malformed input.

### 4.3 `ShortcutRepository` against a real (in-memory) SQLite database

**Why:** `UpdateAsync`'s clear-and-re-add of the `Targets` collection relies on
EF Core cascade-delete-orphan behavior. That's subtle and currently verified only
by manual testing.

**Add:** Tests using `Microsoft.Data.Sqlite` in-memory connection + `AppDbContext`:
add → read back with targets; update replacing the target set (assert no orphaned
`ShortcutTarget` rows remain); delete cascading to targets.

### 4.4 `Device` capability helpers

**Add:** Small tests for `IsIndividualLight` (true for
`devices.types.light`, false for the empty-`Type` group/scenic devices) and the
`Supports*` properties. These encode a real Govee quirk discovered during
development and should be locked down.

### 4.5 Consider bUnit for component logic (optional, larger)

The `DeviceCard` bulk-command dispatch and `Devices.razor` slider-bound
intersection logic are non-trivial and only manually verified. bUnit would cover
them, but adds a dependency and meaningful setup. Treat as optional — only if the
first four items are done and the appetite exists.

---

## Priority 5 — Code quality & maintainability

### 5.1 Add `Directory.Build.props`

**Why:** `TargetFramework`, `Nullable`, `ImplicitUsings`, and
`GenerateDocumentationFile` are duplicated across four `.csproj` files and can
drift.

**Fix:** Centralize in a root `Directory.Build.props`; strip the duplicates from
the individual projects.

### 5.2 Consider `TreatWarningsAsErrors` in CI

**Why:** The build is currently at zero warnings — a good moment to lock that in
so it doesn't erode.

**Fix:** Enable via `Directory.Build.props` (ideally CI-only, so local
work-in-progress builds aren't blocked), e.g.
`<TreatWarningsAsErrors Condition="'$(ContinuousIntegrationBuild)' == 'true'">true</TreatWarningsAsErrors>`.

### 5.3 Don't surface raw exception messages in the UI

**Where:** `Devices.razor`, `Shortcuts.razor`, `DeviceCard.razor` — every
`catch (Exception ex)` interpolates `ex.Message` into `_error`.

**Problem:** Fine for `GoveeApiException` (whose message is genuinely useful —
"Device is offline"), but an unexpected exception type could surface internal
detail (paths, connection strings) to whoever is looking at the page.

**Fix:** Catch `GoveeApiException` specifically and show its message; for other
exception types show a generic message and log the full exception server-side.
Note that no `ILogger` is currently injected into any component — worth adding
as part of this.

### 5.4 Extract duplicated bulk/single command dispatch in `DeviceCard`

**Where:** `src/GoveeController.Web/Components/Shared/DeviceCard.razor` —
`ApplyBulkCommandAsync` and the individual `On*Changed` handlers construct the
same three command+optimistic-update pairs.

**Fix:** Extract small private helpers (`SetBrightnessAsync(int)`,
`SetColorAsync(RgbColor)`, `SetColorTemperatureAsync(int)`) used by both paths.
Cosmetic, but removes a spot where the two paths could drift apart.

### 5.5 `CancellationToken` is never flowed from the UI

**Where:** All Blazor components call service methods without a token.

**Problem:** When a user navigates away mid-request, work continues to completion
and can touch a disposed component.

**Fix:** Add a component-scoped `CancellationTokenSource`, cancel it on dispose,
and pass the token into service calls (every service method already accepts one).
Pairs naturally with item 3.4.

---

## Explicitly *not* recommended

To prevent scope creep, these were considered and deliberately rejected:

- **In-app authentication/login.** Significant scope, easy to get wrong, and the
  documented reverse-proxy approach (2.2) solves the real risk better. Only
  revisit if the user explicitly asks for remote access.
- **Switching from Govee Cloud API to LAN control.** The cloud-only decision was
  deliberate. LAN control would fix rate limiting and latency but is a large
  rewrite of the Infrastructure layer and only works same-subnet.
- **Encrypting Data Protection keys at rest.** See 2.5 — low value here.
- **Replacing SQLite.** Appropriate for the data volume; no reason to change.

---

## Suggested execution order

1. **1.1** (shortcut partial-failure) — real user-facing bug, affects the "All
   Off" shortcut the user actually uses.
2. **1.2 + 4.1** together — fix the fragile parsing and add the tests that should
   have caught it.
3. **3.1** (fail fast on missing key) and **3.3** (HTTPS redirect noise) — small,
   independent, immediately reduce confusion.
4. **2.1 + 2.3** — cheap security wins (docs + CI permissions).
5. **3.2** (rate limiting) — needs a bit of design thought; do it once the
   quicker items are out of the way.
6. **4.2–4.4** — fill remaining test gaps.
7. **3.4–3.7, 5.x** — polish, as appetite allows.

## Verification checklist (run after each item)

```bash
dotnet build                 # expect: 0 warnings, 0 errors
dotnet test                  # expect: all passing (21 today, more as tests are added)
docker compose up --build -d # then exercise the affected flow at http://localhost:8080
docker logs goveecontroller-govee-controller-1 --since 2m   # expect: no new errors/warnings
```

For anything touching command dispatch, confirm against real bulbs — Govee's
eventual-consistency behavior means unit tests alone are not sufficient evidence
that a control path works.

# Govee Controller

A self-hosted web app for controlling Govee smart lights: power, brightness, color, color
temperature, Govee's own dynamic scenes, and your own saved "shortcut" presets — which can be
chained so that applying one runs a short sequence — all from a browser, backed by the
[Govee Cloud API](https://developer.govee.com).

> [!WARNING]
> **Built for local network use only.** This app has no authentication of any kind — anyone who
> can reach it can control every light on your Govee account. Do not port-forward it or otherwise
> expose it to the internet unless you put authentication in front of it first (reverse proxy with
> basic auth, or a VPN like Tailscale/WireGuard). See "Security — read before deploying" below.

## Architecture

Clean Architecture, four layers plus a test project:

```
GoveeController.sln (slnx)
├── src/
│   ├── GoveeController.Domain          # Entities & value objects. No dependencies on anything.
│   ├── GoveeController.Application     # Use-case services + interfaces Infrastructure implements.
│   ├── GoveeController.Infrastructure  # Govee HTTP client, EF Core/SQLite persistence, DI wiring.
│   └── GoveeController.Web             # Blazor Server UI (the composition root).
└── tests/
    └── GoveeController.Application.Tests  # Mocked services, a fake-HTTP Govee client test, and
                                           # repository tests against real in-memory SQLite.
```

Dependency direction is strictly `Web → Application → Domain`, with `Infrastructure` implementing
Application's interfaces (`IGoveeApiClient`, `IShortcutRepository`) and being wired in only from
`Web/Program.cs`. Domain and Application have no knowledge of HTTP, EF Core, or Blazor, which is
what makes them unit-testable without a network connection or a database.

**Domain** (`src/GoveeController.Domain`)
- `Device`, `DeviceCapability`, `LightState`, `RgbColor`, `GoveeScene` — device data as reported
  by Govee, normalized into a shape this app understands.
- `Shortcut` — a user-defined preset (power/brightness/color applied to one or more devices), the
  only thing this app persists itself. Optionally names another shortcut to run after it, or — as a
  composite shortcut — references other shortcuts to run in sequence instead of targeting devices —
  see "Linked shortcuts" and "Composite shortcuts" below.
- `ShortcutTarget` — one device a shortcut applies to; a device-targeted shortcut has at least one.
- `ShortcutReference` — one shortcut a composite shortcut runs; a composite has no device targets of
  its own and instead references these, in order.
- `Schedule` / `ScheduleDays` — a rule that automatically applies a shortcut at a local wall-clock
  time, recurring on one or more days of the week or once at a specific date and time — see
  "Scheduled shortcuts" below.

**Application** (`src/GoveeController.Application`)
- `IGoveeApiClient` / `IShortcutRepository` / `IScheduleRepository` — interfaces Infrastructure implements.
- `IDeviceControlService` / `DeviceControlService` — device listing and control, with a short
  (~12s) in-memory cache on reads to stay well under Govee's 30 requests/minute rate limit.
- `IShortcutService` / `ShortcutService` — CRUD for shortcuts, the chain-linking and composite
  reference rules, and applying a shortcut to all of its devices — or all of its referenced
  shortcuts, for a composite — before continuing down its chain.
- `IScheduleService` / `ScheduleService` — CRUD for schedules and `RunDueSchedulesAsync`, which fires
  every schedule that's due by calling `IShortcutService.ApplyShortcutAsync`. `NextOccurrence` is the
  pure calculator that turns a schedule's local day/time configuration into its next due UTC instant.

**Infrastructure** (`src/GoveeController.Infrastructure`)
- `GoveeApiClient` — talks to `https://openapi.api.govee.com`. Retry/backoff for HTTP 429/5xx is
  configured via `Microsoft.Extensions.Http.Resilience`'s standard resilience handler (see
  `ServiceCollectionExtensions.AddGoveeInfrastructure`), not inside the client itself.
- `AppDbContext` / `ShortcutRepository` / `ScheduleRepository` — EF Core over SQLite, the only
  things this app persists.
- `ScheduleRunnerService` — a hosted `BackgroundService` that ticks every 30 seconds and calls
  `IScheduleService.RunDueSchedulesAsync`. Runs inside this same container/process; scheduling adds
  no extra service, process, or container.
- `ServiceCollectionExtensions.AddGoveeInfrastructure(configuration)` — one-call DI wiring, invoked
  from `Web/Program.cs`.

**Web** (`src/GoveeController.Web`, Blazor Server)
- `Components/Pages/Devices.razor` (`/`) — one `<DeviceCard>` per device: power toggle, a brightness
  number input (0-100), a color picker, a warmth dropdown of the Kelvin values that device actually
  supports, and a dropdown of that device's Govee scenes. Also has "Set All Bulbs" controls that
  broadcast the same brightness/color/warmth to every bulb at once.
- `Components/Pages/Shortcuts.razor` (`/shortcuts`) — list, apply, create, edit, delete, and link
  shortcuts.
- `Components/Pages/Schedules.razor` (`/schedules`) — list, create, edit, delete, and enable/disable
  schedules.
- `Components/Shared/DeviceCard.razor` — the reusable per-device control panel.
- `Program.cs` — the composition root; also runs pending EF Core migrations on startup so the
  SQLite schema self-initializes on first run.

## Linked shortcuts

A shortcut can name another shortcut to run after it, so applying one runs a short sequence. Set the
link with the **"Then run"** dropdown on the Shortcuts page — either while creating a shortcut, or by
editing one that already exists (that's also how you link two shortcuts you made separately, and how
you unlink: set it back to "None").

The rules, all enforced server-side:

- **Chains are capped at 3 shortcuts.** Anything that would make a fourth is rejected.
- **A shortcut can only follow one other shortcut.** If B already runs after A, C can't also claim B —
  you'll get "That shortcut already runs after another shortcut." This is the constraint most likely
  to surprise you; it's what keeps chains simple lines rather than a branching graph.
- **Loops are rejected**, as is linking a shortcut to itself.
- **Each link has its own delay** (0–60 seconds) that runs before the next shortcut starts. See the
  rate-limit note below for why this is more than cosmetic.
- **Applying runs from that shortcut to the end of its chain.** Given `A → B → C`, applying `B` runs
  B then C. Every shortcut keeps its own row and stays independently applyable; the row just shows
  where it leads (`→ then Movie Mode (after 10s)`).
- **Deleting a shortcut that something else runs** clears the link on its predecessor rather than
  deleting or breaking it.

Chains run inline — the Apply button stays busy until the whole chain finishes, which is why the
per-link delay is capped at 60 seconds.

### Chains and the Govee rate limit

Govee allows about **30 requests/minute**. Applying one shortcut costs roughly 3 calls per target
device (power, then brightness, then color/warmth), so a 3-step chain across 6 bulbs is on the order
of 50+ calls — more than can complete in a single minute no matter what the app does.

The per-link delay is the lever that makes large chains work: spacing steps apart puts each step's
burst of calls into a different rate-limit window. For chains covering many bulbs, prefer longer
delays. If you do hit the limit, the app surfaces a "Govee rate limit reached" message and — because
applying is best-effort — later steps still run, so you may end up with a partially applied chain.

## Composite shortcuts

A **composite shortcut** has no device targets of its own — instead it references other existing
shortcuts and runs them in sequence. Think of it as a playlist: one click applies a whole series of
presets, in the order you chose. Pick **"Composite shortcut"** on the Shortcuts page (the type toggle
at the top of the form), then add shortcuts from the dropdown, set a per-reference delay, and
reorder with the ↑/↓ buttons.

- **A shortcut is either composite or device-targeted, never both.** A composite has no power,
  brightness, or color of its own — those live in the shortcuts it references.
- **Each referenced shortcut runs in full**, including its own chain. If your composite references
  "Dim Lights" and "Dim Lights" chains to "Warm White", applying the composite runs both.
- **Each reference has its own delay** (0–60 seconds) before the next referenced shortcut starts —
  the same rate-limit lever as linked shortcuts, so a long playlist can space its bursts of Govee
  calls across rate-limit windows.
- **Composites can reference other composites**, and can themselves be chained from via the
  "Then run" dropdown — subject to the usual loop detection. A reference that would create a cycle
  (directly or through another composite's chain) is rejected.
- **Deleting a shortcut that a composite references** breaks the link (shown as "Missing shortcut")
  rather than deleting the composite; the broken reference is dropped the next time the composite
  is edited and saved.

## Scheduled shortcuts

The **Schedules** page (`/schedules`) automatically applies a saved shortcut at a specific time,
without you clicking Apply — either **recurring** (a time of day plus any combination of Mon–Sun),
or **one-time** (a specific date and time). If the shortcut you schedule is itself linked to
others (see "Linked shortcuts" above), the whole chain runs.

**Set `TZ` before creating schedules.** Schedule times are local wall-clock time in the
container's timezone — see "Changing the container's timezone" above. A container left on the
default UTC will run a "10:00 PM" schedule at 10 PM UTC, not 10 PM wherever you actually are.

A background loop inside this same container checks every 30 seconds for schedules that are due
and applies them — there's no separate scheduler process or container to run.

- **One-time schedules delete themselves after firing.** Once it's run, the row is gone; the app's
  log (`docker logs`) records that it ran. Editing a one-time schedule's date to the past isn't
  possible — the form rejects it.
- **A missed schedule fires late within a 5-minute grace window, otherwise it's skipped.** If the
  container was restarting exactly when a schedule was due, it still fires within about 5 minutes
  of coming back up. Beyond that window (a longer outage, or the container being down for a while),
  that occurrence is skipped rather than firing hours late — a recurring schedule waits for its next
  normal occurrence; a missed one-time schedule is deleted, the same as if it had fired.
- **Disabling a schedule clears its next-run time**; re-enabling it recomputes the next occurrence
  from the current time, so a schedule left disabled for a week doesn't immediately fire the moment
  you turn it back on.
- Scheduled runs count against the same Govee rate-limit budget as manual Apply clicks and chains
  (see above) — if several schedules land at the same time, they run one after another, not in
  parallel.

## Security — read before deploying

**This app has no login and no authentication of any kind, by design** (see "What this app does
not do" below). Anyone who can reach its port can control every light on your Govee account, with
no credential required.

- Fine for a trusted home LAN — that's the intended deployment.
- **Do not port-forward it or expose it to the internet.** An unauthenticated stranger would gain
  full control of your lights, and could exhaust your Govee API key's rate limit as a side effect
  (a denial-of-service on your own lights).
- If you want to control your lights from outside your home network, put something *in front* of
  this app that handles authentication — e.g. a reverse proxy (Caddy, nginx) with basic auth, or
  better, a VPN/mesh network like Tailscale or WireGuard so the app is never internet-facing at
  all. Don't build auth into this app itself for that; a proxy or VPN is simpler and harder to get
  wrong.

## Getting a Govee API key

1. Open the Govee Home app on your phone.
2. Go to your profile → Settings → **Apply for API Key**.
3. Govee emails you a key (usually within a few minutes to a day).

See [developer.govee.com/reference/apply-you-govee-api-key](https://developer.govee.com/reference/apply-you-govee-api-key).

The Govee Cloud API allows **30 requests/minute**, both per-account and per-device. This app's
~12-second read cache (device list, state, scenes) keeps normal UI usage well under that; if you
hit `429 Too Many Requests` anyway, the resilience handler retries with backoff automatically.

## Running with Docker (recommended)

1. Copy the env file and fill in your API key:

   ```bash
   cp .env.example .env
   # then edit .env and set GOVEE_API_KEY=...
   ```

2. Create the host directory the SQLite database will live in, owned by the container's
   non-root user (UID/GID 1654 — see [Dockerfile](Dockerfile)):

   ```bash
   mkdir -p data
   sudo chown 1654:1654 data   # skip sudo if your user already owns it and that's fine locally
   ```

3. Build and start:

   ```bash
   docker compose up --build
   ```

4. Open <http://localhost:8080>.

Shortcuts are persisted in `./data/shortcuts.db` — a real file on the host filesystem
([bind-mounted](https://docs.docker.com/engine/storage/bind-mounts/), not a Docker-managed named
volume — see "Configuration reference" below), so they survive `docker compose down`/`up` and
image rebuilds, and can be backed up, inspected, or moved without touching Docker at all.

`docker-compose.yml` caps the container's logs at 3 × 10MB (auto-rotated by Docker's `json-file`
driver) so `docker logs` output can't grow unbounded and quietly fill the disk — this matters
more than it used to now that the background schedule runner logs a line every 30 seconds.
`docker logs` works normally against the rotated files; nothing else to run or configure.

## Running on a Raspberry Pi

Both `linux/arm64` (Pi 3/4/5 running 64-bit Raspberry Pi OS) and `linux/arm/v7` (32-bit-only
boards, including the original **Pi 2**) are supported — the [CI workflow](.github/workflows/docker-publish.yml)
publishes both architectures to GHCR on every push to `main`.

**Recommended: pull the pre-built image** rather than building on the Pi itself. Building the
.NET SDK locally is slow on any Pi and can be genuinely impractical on lower-RAM boards (the Pi 2
has only 1GB total) — `dotnet publish` alone can approach that ceiling.

1. Install Docker on the Pi, if not already installed:

   ```bash
   curl -fsSL https://get.docker.com | sh
   sudo usermod -aG docker $USER   # log out/in afterward so `docker` works without sudo
   ```

2. Clone the repo (only for its `docker-compose.yml` files — nothing gets built on the Pi):

   ```bash
   git clone https://github.com/bagstac/GoveeController.git
   cd GoveeController
   ```

3. Set up your API key — the only place it ever needs to live; it's never baked into the image or
   committed to the repo:

   ```bash
   cp .env.example .env
   nano .env   # set GOVEE_API_KEY=...
   ```

4. Create the host `./data` directory the same way as in "Running with Docker" above (step 2),
   then start it with the pull-based compose override, [docker-compose.pull.yml](docker-compose.pull.yml)
   — it swaps `docker-compose.yml`'s `build:` for the pre-built GHCR image, so this only ever
   pulls, never builds, while still getting compose's healthcheck/restart-policy/bind-mount
   setup for free:

   ```bash
   docker compose -f docker-compose.yml -f docker-compose.pull.yml up -d
   ```

5. Open `http://<pi-ip-address>:8080` from any device on your network. Find the Pi's address with
   `hostname -I` if you don't already know it.

To pick up a later update:

```bash
docker compose -f docker-compose.yml -f docker-compose.pull.yml pull
docker compose -f docker-compose.yml -f docker-compose.pull.yml up -d
```

**Building from source instead** (e.g. if you're modifying the code) works the same way as any
other Docker deployment — clone the repo and `docker compose up --build -d` — but expect it to be
considerably slower than on a desktop, and best avoided entirely on a 1GB-RAM board like the Pi 2.

## Running locally without Docker

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
# Set your API key for this shell session (or put it in appsettings.Development.json under Govee:ApiKey)
export GOVEE_API_KEY=your-key-here   # PowerShell: $env:GOVEE_API_KEY = "your-key-here"

dotnet run --project src/GoveeController.Web
```

The app listens on the URL printed at startup (typically `https://localhost:5xxx`). The SQLite
database file is created next to the running executable on first launch.

## Running tests

```bash
dotnet test
```

Covers:

- `DeviceControlService`'s caching/invalidation behavior and `ShortcutService`'s validation, apply,
  chain-linking, and composite-reference logic (mocked repository and device-control service — no
  network).
- Chain behavior specifically: every validation rule at its boundary (self-link, already-followed,
  loops, the 3-shortcut cap, delay bounds), step ordering, that the delay runs between steps but not
  after the last one (via `FakeTimeProvider`, so no real waiting), cancellation mid-chain, and
  failure attribution back to the step it came from.
- Composite shortcuts specifically: create/update/apply, that referenced shortcuts run in order and
  in full (including their own chains), per-reference delays, and cycle rejection — both direct
  cycles and indirect ones through another composite's chain or a chain link.
- `ShortcutRepository` against a **real in-memory SQLite database**, not mocks — this is what
  actually verifies the EF Core behavior that's easy to get wrong: cascade-delete of a shortcut's
  targets and composite references, `SetNull` clearing a predecessor's link (or a composite's
  reference) when the shortcut it points at is deleted, and the unique index that stops two
  shortcuts pointing at the same follower.
- `GoveeApiClient`'s request/response mapping against a fake `HttpMessageHandler` (verifies headers,
  request bodies, and error propagation).

## Continuous deployment

[.github/workflows/docker-publish.yml](.github/workflows/docker-publish.yml) runs on every push to
`main`: it runs the test suite as a gate, then builds and pushes a multi-arch image (`linux/amd64`
and `linux/arm/v7`, so it covers Raspberry Pi) to
[ghcr.io/bagstac/govee-controller](https://github.com/bagstac/GoveeController/pkgs/container/govee-controller)
tagged `latest` and with the commit SHA. Nothing needs to be built locally to deploy a new
version — just re-pull and restart via the `docker compose ... pull` / `up -d` commands in the
Raspberry Pi section above (or `docker compose up --build -d` if you built from source).

[.github/workflows/ghcr-cleanup.yml](.github/workflows/ghcr-cleanup.yml) then prunes old package
versions from GHCR — it runs automatically after every successful build, plus once daily as a
safety net. Each push creates a tagged multi-arch manifest plus several untagged
per-platform/attestation children, which pile up fast; this keeps the 15 most recent non-`latest`
tagged builds and deletes anything else older than 2 hours, automatically protecting untagged
versions that a kept tagged build still references. It also has a `workflow_dispatch` trigger with
`dry-run` and `debug-logging` inputs, so you can re-run and inspect it on demand from the Actions
tab instead of only ever seeing it as an unobservable tail of a full build — useful if it ever
looks like nothing got cleaned up again (worth checking the cut-off first: a burst of pushes
means most versions are still younger than the cut-off and correctly not yet eligible, not a bug).

The cleanup job needs a classic PAT with the `read:packages` and `delete:packages` scopes stored
as the `GHCR_CLEANUP_TOKEN` repository secret — `GITHUB_TOKEN` can push and pull images but
[cannot delete package versions](https://github.com/snok/container-retention-policy#token);
GitHub only exposes that via a PAT. Without this secret set, the cleanup job fails on every run
(the build and publish workflow is unaffected — it's a separate workflow).

## Configuration reference

| Setting | Env var (Docker) | Purpose |
|---|---|---|
| `Govee:ApiKey` | `GOVEE_API_KEY` | Required. Your Govee Cloud API key. |
| `ConnectionStrings:ShortcutsDb` | `ConnectionStrings__ShortcutsDb` | SQLite connection string. Defaults to `/data/shortcuts.db` in the container (see `Dockerfile`). |
| — | `TZ` | Container timezone as an [IANA name](https://en.wikipedia.org/wiki/List_of_tz_database_time_zones) (e.g. `America/Chicago`). Defaults to UTC when unset. Anything time-of-day-based inside the container — including scheduled shortcuts — is interpreted in this timezone. |
| — | *(host-side)* `./data` directory | Bind-mounted onto `/data` by `docker-compose.yml`, so `shortcuts.db` and the Data Protection `keys/` folder live directly on the host. Must be owned by UID/GID 1654 (the container's non-root `app` user) before first run — see "Running with Docker" above. |

`GOVEE_API_KEY` is bridged to the `Govee:ApiKey` configuration key explicitly in `Program.cs`,
since ASP.NET Core's environment-variable provider normally expects double-underscore names
(`Govee__ApiKey`) — the single-underscore `GOVEE_API_KEY` name was kept because it is what Govee's
own documentation and tooling conventionally uses.

Secrets are never checked into source control: `.env` is git-ignored, and `.env.example`
documents what to put in it.

### Changing the container's timezone

The container runs in UTC unless told otherwise. To change it, set `TZ` in `.env`:

```bash
TZ=America/Chicago
```

then recreate the container (`docker compose up -d` — a restart alone does not re-read
`.env`). Verify it took effect with:

```bash
docker compose exec govee-controller date
```

which should print the local wall-clock time. The runtime image already includes tzdata, so
any IANA zone name works without Dockerfile changes. Get this right before creating
schedules: schedule times mean wall-clock time in the container's timezone, so a container
left on UTC runs a "10:00 PM" schedule at 10 PM UTC, not 10 PM local.

## What this app does not do

- **No LAN control.** Only the Govee Cloud API is used, by design — it works from anywhere the
  container can reach the internet, at the cost of the cloud API's rate limit. See
  [developer.govee.com](https://developer.govee.com) if you later want to add local UDP control
  for lower latency.
- **No app-level login.** The app assumes it's deployed on a trusted network or behind a reverse
  proxy that handles access control; it does not authenticate users itself. See "Security" above.

## License

[MIT](LICENSE) — see the LICENSE file for the full text.

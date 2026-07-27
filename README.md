# Govee Controller

A self-hosted web app for controlling Govee smart lights: power, brightness, color, color
temperature, Govee's own dynamic scenes, and your own saved "shortcut" presets — all from a
browser, backed by the [Govee Cloud API](https://developer.govee.com).

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
    └── GoveeController.Application.Tests  # Unit tests: mocked services + a fake-HTTP Govee client test.
```

Dependency direction is strictly `Web → Application → Domain`, with `Infrastructure` implementing
Application's interfaces (`IGoveeApiClient`, `IShortcutRepository`) and being wired in only from
`Web/Program.cs`. Domain and Application have no knowledge of HTTP, EF Core, or Blazor, which is
what makes them unit-testable without a network connection or a database.

**Domain** (`src/GoveeController.Domain`)
- `Device`, `DeviceCapability`, `LightState`, `RgbColor`, `GoveeScene` — device data as reported
  by Govee, normalized into a shape this app understands.
- `Shortcut` — a user-defined preset (power/brightness/color for one device), the only thing this
  app persists itself.

**Application** (`src/GoveeController.Application`)
- `IGoveeApiClient` / `IShortcutRepository` — interfaces Infrastructure implements.
- `IDeviceControlService` / `DeviceControlService` — device listing and control, with a short
  (~12s) in-memory cache on reads to stay well under Govee's 30 requests/minute rate limit.
- `IShortcutService` / `ShortcutService` — CRUD for shortcuts and applying one to its device.

**Infrastructure** (`src/GoveeController.Infrastructure`)
- `GoveeApiClient` — talks to `https://openapi.api.govee.com`. Retry/backoff for HTTP 429/5xx is
  configured via `Microsoft.Extensions.Http.Resilience`'s standard resilience handler (see
  `ServiceCollectionExtensions.AddGoveeInfrastructure`), not inside the client itself.
- `AppDbContext` / `ShortcutRepository` — EF Core over SQLite, the only thing this app persists.
- `ServiceCollectionExtensions.AddGoveeInfrastructure(configuration)` — one-call DI wiring, invoked
  from `Web/Program.cs`.

**Web** (`src/GoveeController.Web`, Blazor Server)
- `Components/Pages/Devices.razor` (`/`) — one `<DeviceCard>` per device: power toggle, brightness
  slider, color/color-temperature pickers, and a dropdown of that device's Govee scenes.
- `Components/Pages/Shortcuts.razor` (`/shortcuts`) — list, apply, delete, and create shortcuts.
- `Components/Shared/DeviceCard.razor` — the reusable per-device control panel.
- `Program.cs` — the composition root; also runs pending EF Core migrations on startup so the
  SQLite schema self-initializes on first run.

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

Covers `DeviceControlService`'s caching/invalidation behavior, `ShortcutService`'s validation and
apply logic (all mocked, no network/DB), and `GoveeApiClient`'s request/response mapping against a
fake `HttpMessageHandler` (verifies headers, request bodies, and error propagation).

## Continuous deployment

[.github/workflows/docker-publish.yml](.github/workflows/docker-publish.yml) runs on every push to
`main`: it runs the test suite as a gate, then builds and pushes a multi-arch image (`linux/amd64`
and `linux/arm/v7`, so it covers Raspberry Pi) to
[ghcr.io/bagstac/govee-controller](https://github.com/bagstac/GoveeController/pkgs/container/govee-controller)
tagged `latest` and with the commit SHA. Nothing needs to be built locally to deploy a new
version — just re-pull and restart via the `docker compose ... pull` / `up -d` commands in the
Raspberry Pi section above (or `docker compose up --build -d` if you built from source).

A third job then prunes old package versions from GHCR (each push creates a tagged multi-arch
manifest plus several untagged per-platform/attestation children, which pile up fast). It keeps
the 15 most recent non-`latest` tagged builds and deletes anything else older than a day,
automatically protecting untagged versions that a kept tagged build still references. This job
needs a classic PAT with the `read:packages` and `delete:packages` scopes stored as the
`GHCR_CLEANUP_TOKEN` repository secret — `GITHUB_TOKEN` can push and pull images but [cannot
delete package versions](https://github.com/snok/container-retention-policy#token); GitHub only
exposes that via a PAT. Without this secret set, the cleanup job fails on every run (the build and
publish still succeed regardless — it's an independent job).

## Configuration reference

| Setting | Env var (Docker) | Purpose |
|---|---|---|
| `Govee:ApiKey` | `GOVEE_API_KEY` | Required. Your Govee Cloud API key. |
| `ConnectionStrings:ShortcutsDb` | `ConnectionStrings__ShortcutsDb` | SQLite connection string. Defaults to `/data/shortcuts.db` in the container (see `Dockerfile`). |
| — | *(host-side)* `./data` directory | Bind-mounted onto `/data` by `docker-compose.yml`, so `shortcuts.db` and the Data Protection `keys/` folder live directly on the host. Must be owned by UID/GID 1654 (the container's non-root `app` user) before first run — see "Running with Docker" above. |

`GOVEE_API_KEY` is bridged to the `Govee:ApiKey` configuration key explicitly in `Program.cs`,
since ASP.NET Core's environment-variable provider normally expects double-underscore names
(`Govee__ApiKey`) — the single-underscore `GOVEE_API_KEY` name was kept because it is what Govee's
own documentation and tooling conventionally uses.

Secrets are never checked into source control: `.env` is git-ignored, and `.env.example`
documents what to put in it.

## What this app does not do

- **No LAN control.** Only the Govee Cloud API is used, by design — it works from anywhere the
  container can reach the internet, at the cost of the cloud API's rate limit. See
  [developer.govee.com](https://developer.govee.com) if you later want to add local UDP control
  for lower latency.
- **No app-level login.** The app assumes it's deployed on a trusted network or behind a reverse
  proxy that handles access control; it does not authenticate users itself. See "Security" above.

## License

[MIT](LICENSE) — see the LICENSE file for the full text.

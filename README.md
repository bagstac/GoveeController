# Govee Controller

A self-hosted web app for controlling Govee smart lights: power, brightness, color, color
temperature, Govee's own dynamic scenes, and your own saved "shortcut" presets — all from a
browser, backed by the [Govee Cloud API](https://developer.govee.com).

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

2. Build and start:

   ```bash
   docker compose up --build
   ```

3. Open <http://localhost:8080>.

Shortcuts are persisted in a SQLite database on the `govee-data` named volume (mounted at
`/data` in the container), so they survive `docker compose down`/`up` and image rebuilds — only
`docker volume rm` clears them.

## Running on a Raspberry Pi

The Microsoft .NET Docker images used by the [Dockerfile](Dockerfile) are multi-arch and publish
`linux/arm64` builds, so `docker build`/`docker compose build` automatically pulls the correct
image for the Pi's CPU — no Dockerfile changes needed. Requires a **64-bit** Raspberry Pi OS (Pi 4
or 5); 32-bit OS is not supported by the current .NET base images.

1. Install Docker on the Pi, if not already installed:

   ```bash
   curl -fsSL https://get.docker.com | sh
   sudo usermod -aG docker $USER   # log out/in afterward so `docker` works without sudo
   ```

2. Clone the repo onto the Pi:

   ```bash
   git clone https://github.com/bagstac/GoveeController.git
   cd GoveeController
   ```

3. Set up your API key, same as any Docker deployment (see above) — this is the only place the
   key ever needs to live; it's never baked into the image or committed to the repo:

   ```bash
   cp .env.example .env
   nano .env   # set GOVEE_API_KEY=...
   ```

4. Build and start. Building the SDK image on a Pi is slower than on a desktop (expect several
   minutes on first build) — that's normal, just let it finish:

   ```bash
   docker compose up --build -d
   ```

5. Open `http://<pi-ip-address>:8080` from any device on your network. Find the Pi's address with
   `hostname -I` if you don't already know it.

The container restarts automatically on reboot (`restart: unless-stopped` in
[docker-compose.yml](docker-compose.yml)), so once it's running you shouldn't need to touch the Pi
again unless you're deploying an update — then it's just `git pull && docker compose up --build -d`.

**Building faster (optional):** if the Pi's build times feel too slow for iterating on changes,
build the image on a faster machine with Docker's `buildx` for the Pi's architecture, push it to a
registry (Docker Hub, GHCR), and have the Pi just `docker pull` it instead of building locally:

```bash
# On your dev machine, from the repo root:
docker buildx build --platform linux/arm64 -t <your-registry>/govee-controller:latest --push .
# On the Pi, point docker-compose.yml's `build:` at `image: <your-registry>/govee-controller:latest`
# instead, then: docker compose pull && docker compose up -d
```

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
version — just `docker pull ghcr.io/bagstac/govee-controller:latest` wherever it's running (see
the Raspberry Pi section above for the exact `docker run` command).

## Configuration reference

| Setting | Env var (Docker) | Purpose |
|---|---|---|
| `Govee:ApiKey` | `GOVEE_API_KEY` | Required. Your Govee Cloud API key. |
| `ConnectionStrings:ShortcutsDb` | `ConnectionStrings__ShortcutsDb` | SQLite connection string. Defaults to `/data/shortcuts.db` in the container (see `Dockerfile`). |

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
  proxy that handles access control; it does not authenticate users itself.

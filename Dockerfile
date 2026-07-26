# syntax=docker/dockerfile:1

# --- Build stage -------------------------------------------------------
# Restores and publishes the Web project (which pulls in Domain/Application/Infrastructure via
# project references) as a framework-dependent deployment.
#
# --platform=$BUILDPLATFORM pins this stage to the *host's* architecture even when cross-building
# for another target (e.g. `docker buildx build --platform linux/arm/v7` from an amd64 machine).
# Running the .NET SDK itself under QEMU emulation is unreliable — it can fail outright (observed:
# "MSB4184: The expression '[MSBuild]::GetTargetFrameworkVersion(net6.0)' cannot be evaluated")
# and is far slower regardless. Building natively and cross-publishing for TARGETARCH via the
# .NET runtime identifier (RID) avoids emulating the SDK entirely; only the small runtime-stage
# base image below needs to be the target architecture.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
ARG TARGETVARIANT
WORKDIR /src

# Copy the full source and publish in one step (implicit restore). A csproj-only COPY followed by
# `dotnet restore` + `--no-restore` publish was tried for layer-cache speed, but under BuildKit's
# COPY semantics it intermittently produced a publish output missing the Blazor static web assets
# manifest entries for _framework/blazor.web.js (dev-server runs and a plain `dotnet publish` were
# never affected — only this split restore/publish sequence). Publishing in a single step avoids it.
COPY . .
RUN case "$TARGETARCH$TARGETVARIANT" in \
      amd64) RID=linux-x64 ;; \
      arm64) RID=linux-arm64 ;; \
      armv7) RID=linux-arm ;; \
      *) echo "Unsupported platform: TARGETARCH=$TARGETARCH TARGETVARIANT=$TARGETVARIANT" >&2 && exit 1 ;; \
    esac && \
    dotnet publish src/GoveeController.Web/GoveeController.Web.csproj \
    --configuration Release \
    --runtime "$RID" \
    --self-contained false \
    --output /app/publish

# --- Runtime stage -------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

# Microsoft's aspnet runtime image ships a built-in non-root "app" user (UID 64198) for exactly
# this purpose — least-privilege container, no need to create our own.
RUN mkdir -p /data && chown app:app /data
USER app

ENV ASPNETCORE_HTTP_PORTS=8080
ENV ConnectionStrings__ShortcutsDb="Data Source=/data/shortcuts.db"
EXPOSE 8080

ENTRYPOINT ["dotnet", "GoveeController.Web.dll"]

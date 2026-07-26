using GoveeController.Infrastructure;
using GoveeController.Infrastructure.Persistence;
using GoveeController.Web.Components;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ASP.NET Core's environment-variable configuration provider maps "Govee__ApiKey" (double
// underscore) to "Govee:ApiKey", not the plain "GOVEE_API_KEY" name documented for this app's
// Docker deployment. Bridge the friendlier single name here rather than asking users to set a
// double-underscore variable.
var goveeApiKeyFromEnv = Environment.GetEnvironmentVariable("GOVEE_API_KEY");
if (!string.IsNullOrEmpty(goveeApiKeyFromEnv))
{
    builder.Configuration["Govee:ApiKey"] = goveeApiKeyFromEnv;
}

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// A liveness/readiness probe for docker-compose's `healthcheck:` (see docker-compose.yml) — lets
// `restart: unless-stopped` recover from a hung-but-still-running process, not just a crash.
builder.Services.AddHealthChecks();

// Wires the Govee HTTP client, SQLite shortcut store, caching, and Application-layer use-case
// services. See GoveeController.Infrastructure.ServiceCollectionExtensions for details.
builder.Services.AddGoveeInfrastructure(builder.Configuration);

// Persist Data Protection keys next to the SQLite database (i.e. on the same mounted volume in
// Docker) instead of the container's ephemeral filesystem. Without this, every container restart
// generates a fresh key ring, which invalidates all outstanding antiforgery tokens and breaks any
// form that was open in a browser at the time — including the "new shortcut" form.
//
// This intentionally does not configure an XML encryptor (ASP.NET Core will log a startup warning
// that keys "may be persisted in unencrypted form"). These keys only protect antiforgery tokens —
// there are no user sessions or secrets for them to guard — so encrypting them at rest would add
// real setup complexity (a platform-specific key-encryption-key) for negligible benefit here.
// Accepted trade-off; revisit only if this app ever grows real user sessions.
var shortcutsDbConnectionString = builder.Configuration.GetConnectionString("ShortcutsDb") ?? "Data Source=shortcuts.db";
var dbPath = shortcutsDbConnectionString.Replace("Data Source=", string.Empty, StringComparison.OrdinalIgnoreCase);
var dbDirectory = Path.GetDirectoryName(Path.GetFullPath(dbPath));
if (dbDirectory is not null)
{
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dbDirectory, "keys")));
}

var app = builder.Build();

// Apply any pending EF Core migrations on startup so the container self-initializes its SQLite
// schema on first run without a separate migration step in the deployment process.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

// Only wire up HTTPS redirection/HSTS when an HTTPS endpoint is actually configured. The Docker
// deployment only exposes HTTP (ASPNETCORE_HTTP_PORTS=8080 in the Dockerfile, no HTTPS port), so
// this middleware had nowhere to redirect to and logged "Failed to determine the https port for
// redirect." on every single request — noise that could mask a real problem. Local `dotnet run`
// (which does have an https launch profile) is unaffected.
var hasHttpsEndpoint = builder.Configuration["ASPNETCORE_HTTPS_PORT"] is not null
    || (Environment.GetEnvironmentVariable("ASPNETCORE_URLS")?.Contains("https", StringComparison.OrdinalIgnoreCase) ?? false);
if (hasHttpsEndpoint)
{
    if (!app.Environment.IsDevelopment())
    {
        // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
        app.UseHsts();
    }
    app.UseHttpsRedirection();
}

app.UseAntiforgery();

app.MapHealthChecks("/health");

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

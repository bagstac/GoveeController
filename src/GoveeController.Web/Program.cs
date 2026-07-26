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

// Wires the Govee HTTP client, SQLite shortcut store, caching, and Application-layer use-case
// services. See GoveeController.Infrastructure.ServiceCollectionExtensions for details.
builder.Services.AddGoveeInfrastructure(builder.Configuration);

// Persist Data Protection keys next to the SQLite database (i.e. on the same mounted volume in
// Docker) instead of the container's ephemeral filesystem. Without this, every container restart
// generates a fresh key ring, which invalidates all outstanding antiforgery tokens and breaks any
// form that was open in a browser at the time — including the "new shortcut" form.
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
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

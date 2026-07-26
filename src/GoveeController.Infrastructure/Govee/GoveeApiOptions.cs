namespace GoveeController.Infrastructure.Govee;

/// <summary>
/// Configuration for talking to the Govee Cloud API. Bound from configuration section "Govee";
/// in the Docker deployment this is populated by the <c>GOVEE_API_KEY</c> environment variable
/// (ASP.NET Core's environment-variable configuration provider maps it to <c>Govee:ApiKey</c>).
/// </summary>
public sealed class GoveeApiOptions
{
    /// <summary>Configuration section name this class binds to.</summary>
    public const string SectionName = "Govee";

    /// <summary>
    /// The API key issued by Govee (Govee Home app -> profile -> "Apply for API Key"). Required.
    /// Never checked into source control — supplied via environment variable or Docker secret.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Base URL of the Govee Open API. Overridable for testing against a mock server.</summary>
    public string BaseUrl { get; set; } = "https://openapi.api.govee.com";
}

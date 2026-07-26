namespace GoveeController.Infrastructure.Govee;

/// <summary>
/// Thrown when the Govee API responds with HTTP 200 but an application-level failure code in the
/// response body — which is how Govee reports device-specific problems (e.g. "Device is offline")
/// rather than using HTTP status codes for them.
/// </summary>
public sealed class GoveeApiException : Exception
{
    /// <summary>Govee's numeric error code from the response body (distinct from the HTTP status code).</summary>
    public int GoveeCode { get; }

    /// <summary>Creates the exception.</summary>
    public GoveeApiException(int goveeCode, string message) : base(message)
    {
        GoveeCode = goveeCode;
    }
}

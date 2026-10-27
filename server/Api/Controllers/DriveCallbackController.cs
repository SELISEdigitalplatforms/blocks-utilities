using BlocksTemplate.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Utility.DomainService.Drive.Service;

namespace Api.Controllers;

/// <summary>
/// The Entra redirect target for the drive connection flow (spec 004).
/// </summary>
/// <remarks>
/// Deliberately outside the <c>api</c> prefix, like the payment and SMS webhooks. Genesis's tenant
/// validation middleware guards everything under <c>api/</c> and refuses any <c>Origin</c> or
/// <c>Referer</c> that is not one of the project's registered domains; Entra's redirect arrives
/// with a <c>login.microsoftonline.com</c> referer and was answered 406 before this action ran.
/// The route is the <c>Drive:RedirectUrl</c> path and must match the Entra app registration.
/// </remarks>
[ApiController]
[AllowAnonymous]
[SkipGlobalApiRoutePrefix]
[Route("drive/connections/callback")]
public sealed class DriveCallbackController : ControllerBase
{
    private readonly IDriveConnectionService _connections;

    /// <summary>Initializes a new instance of the <see cref="DriveCallbackController"/> class.</summary>
    public DriveCallbackController(IDriveConnectionService connections) => _connections = connections;

    /// <summary>
    /// Where Entra sends the browser after consent. Finishes the connection and redirects to the
    /// caller's <c>returnUrl</c>.
    /// </summary>
    /// <remarks>
    /// Anonymous by necessity. 400 only when there is nowhere safe to redirect: an unknown, replayed
    /// or expired state. Every other outcome, including a refused consent, goes back to the
    /// <c>returnUrl</c> stored with the state so the signature app can tell the user. An
    /// admin-consent round trip (<c>admin_consent=True</c>, no state) gets a plain text page, because
    /// it was an IT admin in a separate browser, not the user's flow.
    /// </remarks>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status302Found)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Callback(
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery] string? error,
        [FromQuery(Name = "error_description")] string? errorDescription,
        [FromQuery(Name = "admin_consent")] string? adminConsent,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(state) && !string.IsNullOrEmpty(adminConsent))
        {
            return Content(
                string.Equals(adminConsent, "True", StringComparison.OrdinalIgnoreCase)
                    ? "Admin consent recorded. Users in your organization can now connect their drives. You can close this window."
                    : "Admin consent was not granted.",
                "text/plain");
        }

        var outcome = await _connections.CompleteAsync(code, state, error, errorDescription, cancellationToken);
        if (outcome.RedirectUrl is null)
        {
            return BadRequest("Unknown or expired state.");
        }

        return Redirect(outcome.RedirectUrl);
    }
}

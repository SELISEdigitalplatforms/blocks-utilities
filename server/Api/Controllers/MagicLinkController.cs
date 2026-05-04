using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Utility.DomainService.MagicLink.Models;
using Utility.DomainService.MagicLink.Service;
using Utility.DomainService.MagicLink;

namespace Api.Controllers
{
    /// <summary>
    /// Unified API Controller for magic link operations.
    /// Combines URL shortener (Redirect type) and link-to-action (Action type) functionality.
    /// Uses MagicLinks collection with Type property to distinguish between link types.
    /// </summary>
    [ApiController]
    [Route("[controller]/[action]")]
    public class MagicLinkController : ControllerBase
    {
        private readonly IMagicLinkService _magicLinkService;
        private readonly IConfiguration _configuration;

        /// <summary>
        /// Initializes a new instance of the <see cref="MagicLinkController"/> class.
        /// </summary>
        /// <param name="magicLinkService">The magic link service.</param>
        /// <param name="configuration">The application configuration.</param>
        public MagicLinkController(
            IMagicLinkService magicLinkService,
            IConfiguration configuration)
        {
            _magicLinkService = magicLinkService;
            _configuration = configuration;
        }

        /// <summary>
        /// Creates a single magic link.
        /// </summary>
        /// <remarks>
        /// Creates a magic link that can be either:
        /// - Redirect type: Simple URL shortening with redirect functionality
        /// - Action type: Maps to an HTTP action that is executed when the link is invoked
        /// 
        /// Request parameters:
        /// - Type: MagicLinkType.Redirect (0) or MagicLinkType.Action (1)
        /// - Uri: Target URL (redirect URL for Redirect type, API endpoint for Action type)
        /// - Name: Optional friendly name for the link
        /// - UriOnForbidden: Geo-restricted redirect URL (Redirect type only)
        /// - RequestMethod: HTTP method for Action type (GET, POST, PUT, DELETE)
        /// - RequestPayload: JSON string payload for Action type POST/PUT requests
        /// - RequestHeaders: JSON string of headers for Action type
        /// - RedirectUrl: URL to redirect after action is performed (Action type only)
        /// - UsageLimit: Maximum number of times the link can be used (0 = unlimited)
        /// - ExpiryLifeSpan: Link expiration in milliseconds (0 = no expiration)
        /// - Persistent: Whether the link should be permanent
        /// - ProjectKey: Project/tenant key for multi-tenancy
        /// </remarks>
        /// <param name="request">The link creation request</param>
        /// <returns>Response containing the generated link ID and short URI</returns>
        [HttpPost]
        [Authorize]
        public async Task<CreateMagicLinkResponse> CreateLink([FromBody] CreateMagicLinkRequest request)
        {
            return await _magicLinkService.CreateLinkAsync(request);
        }

        /// <summary>
        /// Creates multiple magic links in bulk.
        /// </summary>
        /// <remarks>
        /// Generates multiple magic links in a single request.
        /// Each link can be either Redirect or Action type.
        /// 
        /// Request parameters:
        /// - Requests: Array of CreateMagicLinkRequest objects
        /// - ProjectKey: Default project key for all links (can be overridden per link)
        /// </remarks>
        /// <param name="request">The bulk link creation request</param>
        /// <returns>Response containing all generated links with their IDs and success count</returns>
        [HttpPost]
        [Authorize]
        public async Task<CreateMagicLinksResponse> CreateLinks([FromBody] CreateMagicLinksRequest request)
        {
            return await _magicLinkService.CreateLinksAsync(request);
        }

        /// <summary>
        /// Removes multiple magic links by their IDs.
        /// </summary>
        /// <remarks>
        /// Disables the specified links, making them no longer accessible.
        /// The links are removed from the cache and marked as disabled in the database.
        /// 
        /// Request parameters:
        /// - LinkIds: Array of link IDs to remove
        /// - ProjectKey: Project/tenant key for multi-tenancy
        /// </remarks>
        /// <param name="request">The link removal request</param>
        /// <returns>Response indicating success and number of links removed</returns>
        [HttpPost]
        [Authorize]
        public async Task<RemoveMagicLinksResponse> RemoveLinks([FromBody] RemoveMagicLinksRequest request)
        {
            return await _magicLinkService.RemoveLinksAsync(request);
        }

        /// <summary>
        /// Get a single magic link by ID.
        /// </summary>
        /// <remarks>
        /// Retrieves a specific magic link by its ID.
        /// The response includes the computed status of the link:
        /// - Active: Link is functional and not expired
        /// - TimeExpired: Link has passed its expiry date
        /// - UsageLimitExceeded: Link usage limit has been reached
        /// - ManuallyDisabled: Link was manually disabled
        /// 
        /// Parameters:
        /// - ItemId: The unique identifier of the link
        /// - ProjectKey: The project/tenant identifier
        /// </remarks>
        /// <param name="request">The request containing the link ID and project key</param>
        /// <returns>Response containing the link details with computed status</returns>
        [HttpGet]
        [Authorize]
        public async Task<GetMagicLinkResponse> GetLink([FromQuery] GetMagicLinkRequest request)
        {
            return await _magicLinkService.GetLinkAsync(request);
        }

        /// <summary>
        /// Get a paginated list of magic links for a project.
        /// </summary>
        /// <remarks>
        /// Retrieves multiple magic links for administrative purposes, supporting pagination.
        /// 
        /// Parameters:
        /// - PageSize: Number of items per page (default: 10)
        /// - PageNumber: Page index starting from 0 (default: 0)
        /// - ProjectKey: The project/tenant identifier
        /// - Type: Optional filter by link type (Action or Redirect)
        /// - SearchText: Optional search text (searches Name and Uri)
        /// - Status: Optional filter by status (Active, TimeExpired, UsageLimitExceeded, ManuallyDisabled)
        /// - RequestMethod: Optional filter by RequestMethod (for Action type)
        /// - ExpiryDateRange: Optional filter by expiry date range
        /// </remarks>
        /// <param name="request">The request containing pagination and filter information</param>
        /// <returns>Response containing the list of links with status information and total count</returns>
        [HttpGet]
        [Authorize]
        public async Task<GetMagicLinksResponse> GetLinks([FromQuery] GetMagicLinksRequest request)
        {
            return await _magicLinkService.GetLinksAsync(request);
        }

        /// <summary>
        /// Saves (creates or updates) a LinkBasedActionConfig for a project.
        /// </summary>
        /// <remarks>
        /// This endpoint creates or updates the configuration for link-based actions.
        /// If no configuration exists for the project, a new one is created.
        /// If a configuration already exists, it is updated with the new values.
        /// 
        /// Request parameters:
        /// - ContextName: Context name for the configuration
        /// - ShortUrlBase: Base URL for generating short URLs (e.g., https://short.example.com/)
        /// - ProjectKey: Project/tenant key for multi-tenancy
        /// </remarks>
        /// <param name="request">The configuration save request</param>
        /// <returns>Response containing the saved configuration and whether it was created or updated</returns>
        [HttpPost]
        [Authorize]
        public async Task<SaveLinkBasedActionConfigResponse> SaveConfig([FromBody] SaveLinkBasedActionConfigRequest request)
        {
            return await _magicLinkService.SaveLinkBasedActionConfigAsync(request);
        }

        /// <summary>
        /// Gets the LinkBasedActionConfig for a project.
        /// </summary>
        /// <remarks>
        /// Retrieves the configuration for link-based actions for the specified project.
        /// Returns null if no configuration exists.
        /// 
        /// Parameters:
        /// - ProjectKey: The project/tenant identifier
        /// </remarks>
        /// <param name="request">The request containing the project key</param>
        /// <returns>Response containing the configuration (if exists)</returns>
        [HttpGet]
        [Authorize]
        public async Task<GetLinkBasedActionConfigResponse> GetConfig([FromQuery] GetLinkBasedActionConfigRequest request)
        {
            return await _magicLinkService.GetLinkBasedActionConfigAsync(request);
        }

        /// <summary>
        /// Invoke a magic link (public endpoint for direct link access).
        /// </summary>
        /// <remarks>
        /// This endpoint provides the invocation functionality for both link types:
        /// 
        /// For Redirect type:
        /// - Validates the link exists and is not expired
        /// - Sends usage tracking event (async)
        /// - Returns HTTP redirect to the target URL
        /// 
        /// For Action type:
        /// - Validates the link exists and is not expired
        /// - Queues the action for background processing
        /// - Returns redirect to post-action URL if configured, or success response
        /// 
        /// URL Format: /MagicLink/Invoke/{linkId}
        /// 
        /// The endpoint will return:
        /// - 302 Redirect for Redirect type
        /// - 302 Redirect to post-action URL for Action type (if configured)
        /// - 200 OK with success message for Action type (if no redirect URL)
        /// - 404 Not Found if the link doesn't exist
        /// - 410 Gone if the link is expired or limit exceeded
        /// </remarks>
        /// <param name="linkId">The link ID from the URL path</param>
        /// <param name="projectKey">Optional project key from query string</param>
        /// <param name="subscriptionFilterId">Optional subscription filter ID for notifications</param>
        /// <returns>HTTP redirect response or JSON response</returns>
        [HttpGet("{linkId}")]
        [AllowAnonymous]
        public async Task<IActionResult> Invoke(
            string linkId,
            [FromQuery] string? projectKey = null,
            [FromQuery] string? subscriptionFilterId = null)
        {
            try
            {
                // Use RootTenantId if projectKey not provided
                //var effectiveProjectKey = projectKey ?? _configuration["RootTenantId"];
                var effectiveProjectKey = projectKey ?? "f080a1bea04280a72149fd689d50a48c";

                var request = new InvokeMagicLinkRequest
                {
                    LinkId = linkId,
                    ProjectKey = effectiveProjectKey,
                    SubscriptionFilterId = subscriptionFilterId,
                    NotifyOnProcessEnding = !string.IsNullOrEmpty(subscriptionFilterId),
                    VisitorIpAddress = GetVisitorIpAddress(),
                    VisitorUserAgent = GetVisitorUserAgent(),
                    VisitorOrigin = GetVisitorOrigin(),
                    VisitorLanguage = GetVisitorLanguage()
                };

                var result = await _magicLinkService.InvokeLinkAsync(request);

                // If successful and has redirect URL, redirect the user
                if (result.IsSuccess && !string.IsNullOrEmpty(result.RedirectUrl))
                {
                    return RedirectPermanent(result.RedirectUrl);
                }

                // If failed, return appropriate error response
                if (!result.IsSuccess)
                {
                    return result.ErrorCode switch
                    {
                        "LINK_NOT_FOUND" => NotFound(new { error = result.ErrorMessage }),
                        "LINK_EXPIRED" or "LINK_LIMIT_EXCEEDED" => StatusCode(410, new { error = result.ErrorMessage }), // 410 Gone
                        _ => BadRequest(new { error = result.ErrorMessage })
                    };
                }

                // Action type with no redirect URL configured - return success HTML
                if (result.Type == MagicLinkType.Action.ToString())
                {
                    return Content(@"
                        <html>
                        <head>
                            <meta charset=""UTF-8"">
                            <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
                            <title>Action Queued</title>
                            <style>
                                body {
                                    font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Oxygen, Ubuntu, sans-serif;
                                    display: flex;
                                    justify-content: center;
                                    align-items: center;
                                    min-height: 100vh;
                                    margin: 0;
                                    background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
                                }
                                .container {
                                    text-align: center;
                                    background: white;
                                    padding: 3rem;
                                    border-radius: 16px;
                                    box-shadow: 0 25px 50px -12px rgba(0, 0, 0, 0.25);
                                }
                                h2 {
                                    color: #1a202c;
                                    margin-bottom: 0.5rem;
                                }
                                p {
                                    color: #718096;
                                }
                                .checkmark {
                                    width: 80px;
                                    height: 80px;
                                    margin: 0 auto 1.5rem;
                                    background: #48bb78;
                                    border-radius: 50%;
                                    display: flex;
                                    align-items: center;
                                    justify-content: center;
                                }
                                .checkmark svg {
                                    width: 40px;
                                    height: 40px;
                                    stroke: white;
                                    stroke-width: 3;
                                    fill: none;
                                }
                            </style>
                        </head>
                        <body>
                            <div class=""container"">
                                <div class=""checkmark"">
                                    <svg viewBox=""0 0 24 24""><polyline points=""20 6 9 17 4 12""></polyline></svg>
                                </div>
                                <h2>Action queued successfully!</h2>
                                <p>Your request is being processed.</p>
                                <p style=""font-size: 0.875rem; margin-top: 1.5rem;"">You may now close this tab.</p>
                            </div>
                            <script>
                                // Attempt to close - works if opened via window.open()
                                setTimeout(function() { window.close(); }, 2000);
                            </script>
                        </body>
                        </html>", "text/html");
                }

                // Redirect type with no redirect URL (shouldn't happen, but handle gracefully)
                return Ok(new { message = "Link invoked successfully", type = result.Type });
            }
            catch (Exception)
            {
                return StatusCode(500, "Internal server error occurred during link invocation");
            }
        }

        /// <summary>
        /// Get visitor IP address from request headers.
        /// </summary>
        /// <returns>The visitor's IP address</returns>
        private string? GetVisitorIpAddress()
        {
            const string X_Forwarded_For_Header_Name = "X-Forwarded-For";

            var forwardedForHeader = Request.Headers[X_Forwarded_For_Header_Name].FirstOrDefault();

            var visitorsIpAddress = string.IsNullOrWhiteSpace(forwardedForHeader)
                ? Request.HttpContext.Connection.RemoteIpAddress?.ToString()
                : forwardedForHeader.Split(',', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();

            return visitorsIpAddress;
        }

        /// <summary>
        /// Get visitor's browser and OS information from User-Agent header.
        /// </summary>
        /// <returns>The User-Agent string containing browser and OS information</returns>
        private string? GetVisitorUserAgent()
        {
            return Request.Headers["User-Agent"].FirstOrDefault();
        }

        /// <summary>
        /// Get the origin of the request.
        /// </summary>
        /// <returns>The origin URL, or referer if origin is not available</returns>
        private string? GetVisitorOrigin()
        {
            // Origin header is sent with CORS requests and form submissions
            var origin = Request.Headers["Origin"].FirstOrDefault();
            
            // Fallback to Referer header if Origin is not available
            if (string.IsNullOrWhiteSpace(origin))
            {
                origin = Request.Headers["Referer"].FirstOrDefault();
            }

            return origin;
        }

        /// <summary>
        /// Get visitor's preferred language from Accept-Language header.
        /// </summary>
        /// <returns>The Accept-Language header value</returns>
        private string? GetVisitorLanguage()
        {
            return Request.Headers["Accept-Language"].FirstOrDefault();
        }
    }
}


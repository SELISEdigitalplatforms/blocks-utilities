using DomainService.Entities;
using DomainService.OAuth.RequestModel;
using DomainService.Utilities;
using FluentAssertions;
using Xunit;

namespace XUnitTest.DomainService.Utilities
{
    public class HelperTests
    {
        #region LoadAuthorizationHtmlContent Tests

        [Fact]
        public void LoadAuthorizationHtmlContent_WithValidInputs_ReturnsCompiledHtml()
        {
            // Arrange
            var templateContent = "<html><body>Client: {{client_id}}, User: {{Username}}</body></html>";
            var loginUrl = "https://example.com/login";
            var apiKey = "test-api-key";
            var username = "testuser";
            var request = new AuthorizeRequest
            {
                State = "test-state",
                Nonce = "test-nonce",
                ClientId = "client-123",
                RedirectUri = "https://example.com/callback"
            };
            var clientCredential = new OIDCClientCredential
            {
                ItemId = "client-123",
                RedirectUri = "https://example.com/callback",
                Scope = "openid profile"
            };

            // Act
            var result = Helper.LoadAuthorizationHtmlContent(templateContent, loginUrl, apiKey, username, request, clientCredential);

            // Assert
            result.Should().NotBeNullOrEmpty();
            result.Should().Contain("Client: client-123");
            result.Should().Contain("User: testuser");
        }

        [Fact]
        public void LoadAuthorizationHtmlContent_WithAllTemplateVariables_ReplacesAllValues()
        {
            // Arrange
            var templateContent = @"
                <html>
                    <body>
                        <p>Response Type: {{response_type}}</p>
                        <p>Client ID: {{client_id}}</p>
                        <p>Redirect URI: {{redirect_uri}}</p>
                        <p>Scope: {{Scope}}</p>
                        <p>State: {{State}}</p>
                        <p>Nonce: {{Nonce}}</p>
                        <p>Login URL: {{LoginEndpointUrl}}</p>
                        <p>Acknowledge URI: {{AcknowledgeUri}}</p>
                        <p>API Key: {{XBlocksKey}}</p>
                        <p>Username: {{Username}}</p>
                        <p>User: {{user}}</p>
                    </body>
                </html>";
            var loginUrl = "https://example.com/login";
            var apiKey = "test-api-key-123";
            var username = "john.doe@example.com";
            var request = new AuthorizeRequest
            {
                State = "state-abc-123",
                Nonce = "nonce-xyz-789",
                ClientId = "client-456",
                RedirectUri = "https://app.example.com/callback"
            };
            var clientCredential = new OIDCClientCredential
            {
                ItemId = "client-456",
                RedirectUri = "https://app.example.com/callback",
                Scope = "openid profile email"
            };

            // Act
            var result = Helper.LoadAuthorizationHtmlContent(templateContent, loginUrl, apiKey, username, request, clientCredential);

            // Assert
            result.Should().Contain("Response Type: code");
            result.Should().Contain("Client ID: client-456");
            result.Should().Contain("Redirect URI: https://app.example.com/callback");
            result.Should().Contain("Scope: openid profile email");
            result.Should().Contain("State: state-abc-123");
            result.Should().Contain("Nonce: nonce-xyz-789");
            result.Should().Contain("Login URL: https://example.com/login");
            result.Should().Contain("Acknowledge URI: https://example.com/login");
            result.Should().Contain("API Key: test-api-key-123");
            result.Should().Contain("Username: john.doe@example.com");
            result.Should().Contain("User: john.doe@example.com");
        }

        [Fact]
        public void LoadAuthorizationHtmlContent_WithNullRedirectUri_UsesEmptyString()
        {
            // Arrange
            var templateContent = "<html><body>Redirect: {{redirect_uri}}</body></html>";
            var loginUrl = "https://example.com/login";
            var apiKey = "test-api-key";
            var username = "testuser";
            var request = new AuthorizeRequest
            {
                State = "test-state",
                ClientId = "client-123"
            };
            var clientCredential = new OIDCClientCredential
            {
                ItemId = "client-123",
                RedirectUri = null,
                Scope = "openid"
            };

            // Act
            var result = Helper.LoadAuthorizationHtmlContent(templateContent, loginUrl, apiKey, username, request, clientCredential);

            // Assert
            result.Should().Contain("Redirect: ");
            result.Should().NotContain("Redirect: null");
        }

        [Fact]
        public void LoadAuthorizationHtmlContent_WithNullScope_UsesEmptyString()
        {
            // Arrange
            var templateContent = "<html><body>Scope: {{Scope}}</body></html>";
            var loginUrl = "https://example.com/login";
            var apiKey = "test-api-key";
            var username = "testuser";
            var request = new AuthorizeRequest
            {
                State = "test-state",
                ClientId = "client-123"
            };
            var clientCredential = new OIDCClientCredential
            {
                ItemId = "client-123",
                RedirectUri = "https://example.com/callback",
                Scope = null
            };

            // Act
            var result = Helper.LoadAuthorizationHtmlContent(templateContent, loginUrl, apiKey, username, request, clientCredential);

            // Assert
            result.Should().Contain("Scope: ");
            result.Should().NotContain("Scope: null");
        }

        [Fact]
        public void LoadAuthorizationHtmlContent_WithNullStateAndNonce_RendersNullValues()
        {
            // Arrange
            var templateContent = "<html><body>State: {{State}}, Nonce: {{Nonce}}</body></html>";
            var loginUrl = "https://example.com/login";
            var apiKey = "test-api-key";
            var username = "testuser";
            var request = new AuthorizeRequest
            {
                State = null,
                Nonce = null,
                ClientId = "client-123"
            };
            var clientCredential = new OIDCClientCredential
            {
                ItemId = "client-123",
                RedirectUri = "https://example.com/callback",
                Scope = "openid"
            };

            // Act
            var result = Helper.LoadAuthorizationHtmlContent(templateContent, loginUrl, apiKey, username, request, clientCredential);

            // Assert
            result.Should().NotBeNullOrEmpty();
            result.Should().Contain("State: ");
            result.Should().Contain("Nonce: ");
        }

        [Fact]
        public void LoadAuthorizationHtmlContent_WithComplexHtmlTemplate_PreservesHtmlStructure()
        {
            // Arrange
            var templateContent = @"
<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <title>Authorization</title>
</head>
<body>
    <div class=""container"">
        <h1>Welcome {{Username}}</h1>
        <form action=""{{LoginEndpointUrl}}"" method=""post"">
            <input type=""hidden"" name=""client_id"" value=""{{client_id}}"" />
            <input type=""hidden"" name=""state"" value=""{{State}}"" />
            <button type=""submit"">Authorize</button>
        </form>
    </div>
</body>
</html>";
            var loginUrl = "https://idp.example.com/oauth/login";
            var apiKey = "api-key-xyz";
            var username = "alice@example.com";
            var request = new AuthorizeRequest
            {
                State = "random-state",
                Nonce = "random-nonce",
                ClientId = "web-client-1"
            };
            var clientCredential = new OIDCClientCredential
            {
                ItemId = "web-client-1",
                RedirectUri = "https://webapp.example.com/callback",
                Scope = "openid profile email"
            };

            // Act
            var result = Helper.LoadAuthorizationHtmlContent(templateContent, loginUrl, apiKey, username, request, clientCredential);

            // Assert
            result.Should().Contain("<!DOCTYPE html>");
            result.Should().Contain("<title>Authorization</title>");
            result.Should().Contain("Welcome alice@example.com");
            result.Should().Contain("action=\"https://idp.example.com/oauth/login\"");
            result.Should().Contain("value=\"web-client-1\"");
            result.Should().Contain("value=\"random-state\"");
        }

        [Fact]
        public void LoadAuthorizationHtmlContent_WithEmptyUsername_RendersEmptyValue()
        {
            // Arrange
            var templateContent = "<html><body>User: {{Username}}</body></html>";
            var loginUrl = "https://example.com/login";
            var apiKey = "test-api-key";
            var username = "";
            var request = new AuthorizeRequest
            {
                ClientId = "client-123"
            };
            var clientCredential = new OIDCClientCredential
            {
                ItemId = "client-123"
            };

            // Act
            var result = Helper.LoadAuthorizationHtmlContent(templateContent, loginUrl, apiKey, username, request, clientCredential);

            // Assert
            result.Should().Contain("User: ");
        }

        #endregion

        #region GetAuthorizationError Tests

        [Fact]
        public void GetAuthorizationError_WithNullClientCredential_ReturnsClientNotFoundError()
        {
            // Arrange
            var errorPageUri = "https://example.com/error";
            var request = new AuthorizeRequest
            {
                ClientId = "non-existent-client",
                RedirectUri = "https://example.com/callback",
                Scope = "openid"
            };
            OIDCClientCredential clientCredential = null;

            // Act
            var result = Helper.GetAuthorizationError(errorPageUri, request, clientCredential);

            // Assert
            result.Should().Contain("https://example.com/error");
            result.Should().Contain("title=Client not found");
            result.Should().Contain("code=invalid_client");
            result.Should().Contain("messge=no client exist with clientId non-existent-client");
        }

        [Fact]
        public void GetAuthorizationError_WithRedirectUriMismatch_ReturnsRedirectUriError()
        {
            // Arrange
            var errorPageUri = "https://example.com/error";
            var request = new AuthorizeRequest
            {
                ClientId = "client-123",
                RedirectUri = "https://wrong.example.com/callback",
                Scope = "openid"
            };
            var clientCredential = new OIDCClientCredential
            {
                ItemId = "client-123",
                RedirectUri = "https://correct.example.com/callback",
                Scope = "openid"
            };

            // Act
            var result = Helper.GetAuthorizationError(errorPageUri, request, clientCredential);

            // Assert
            result.Should().Contain("https://example.com/error");
            result.Should().Contain("title=Redirect URI mismatch");
            result.Should().Contain("code=invalid_request");
            result.Should().Contain("messge=The redirect_uri https://wrong.example.com/callback does not match the registered redirect URI.");
        }

        [Fact]
        public void GetAuthorizationError_WithScopeMismatch_ReturnsScopeError()
        {
            // Arrange
            var errorPageUri = "https://example.com/error";
            var request = new AuthorizeRequest
            {
                ClientId = "client-123",
                RedirectUri = "https://example.com/callback",
                Scope = "openid profile email"
            };
            var clientCredential = new OIDCClientCredential
            {
                ItemId = "client-123",
                RedirectUri = "https://example.com/callback",
                Scope = "openid profile"
            };

            // Act
            var result = Helper.GetAuthorizationError(errorPageUri, request, clientCredential);

            // Assert
            result.Should().Contain("https://example.com/error");
            result.Should().Contain("title=Scope mismatch");
            result.Should().Contain("code=invalid_scope");
            result.Should().Contain("messge=The scope openid profile email does not match the registered scope.");
        }

        [Fact]
        public void GetAuthorizationError_WithNoErrors_ReturnsPlainErrorPageUri()
        {
            // Arrange
            var errorPageUri = "https://example.com/error";
            var request = new AuthorizeRequest
            {
                ClientId = "client-123",
                RedirectUri = "https://example.com/callback",
                Scope = "openid profile"
            };
            var clientCredential = new OIDCClientCredential
            {
                ItemId = "client-123",
                RedirectUri = "https://example.com/callback",
                Scope = "openid profile"
            };

            // Act
            var result = Helper.GetAuthorizationError(errorPageUri, request, clientCredential);

            // Assert
            result.Should().Be("https://example.com/error");
            result.Should().NotContain("?");
        }

        [Fact]
        public void GetAuthorizationError_WithNullRedirectUris_DetectsMismatch()
        {
            // Arrange
            var errorPageUri = "https://example.com/error";
            var request = new AuthorizeRequest
            {
                ClientId = "client-123",
                RedirectUri = null,
                Scope = "openid"
            };
            var clientCredential = new OIDCClientCredential
            {
                ItemId = "client-123",
                RedirectUri = "https://example.com/callback",
                Scope = "openid"
            };

            // Act
            var result = Helper.GetAuthorizationError(errorPageUri, request, clientCredential);

            // Assert
            result.Should().Contain("title=Redirect URI mismatch");
            result.Should().Contain("code=invalid_request");
        }

        [Fact]
        public void GetAuthorizationError_WithNullScopes_DetectsMismatch()
        {
            // Arrange
            var errorPageUri = "https://example.com/error";
            var request = new AuthorizeRequest
            {
                ClientId = "client-123",
                RedirectUri = "https://example.com/callback",
                Scope = null
            };
            var clientCredential = new OIDCClientCredential
            {
                ItemId = "client-123",
                RedirectUri = "https://example.com/callback",
                Scope = "openid"
            };

            // Act
            var result = Helper.GetAuthorizationError(errorPageUri, request, clientCredential);

            // Assert
            result.Should().Contain("title=Scope mismatch");
            result.Should().Contain("code=invalid_scope");
        }

        [Fact]
        public void GetAuthorizationError_PrioritizesClientNotFound_OverOtherErrors()
        {
            // Arrange
            var errorPageUri = "https://example.com/error";
            var request = new AuthorizeRequest
            {
                ClientId = "non-existent-client",
                RedirectUri = "https://wrong.example.com/callback",
                Scope = "wrong-scope"
            };
            OIDCClientCredential clientCredential = null;

            // Act
            var result = Helper.GetAuthorizationError(errorPageUri, request, clientCredential);

            // Assert
            result.Should().Contain("code=invalid_client");
            result.Should().NotContain("code=invalid_request");
            result.Should().NotContain("code=invalid_scope");
        }

        [Fact]
        public void GetAuthorizationError_WithBothRedirectAndScopeMismatch_PrioritizesRedirectError()
        {
            // Arrange
            var errorPageUri = "https://example.com/error";
            var request = new AuthorizeRequest
            {
                ClientId = "client-123",
                RedirectUri = "https://wrong.example.com/callback",
                Scope = "wrong-scope"
            };
            var clientCredential = new OIDCClientCredential
            {
                ItemId = "client-123",
                RedirectUri = "https://correct.example.com/callback",
                Scope = "openid"
            };

            // Act
            var result = Helper.GetAuthorizationError(errorPageUri, request, clientCredential);

            // Assert
            result.Should().Contain("code=invalid_request");
            result.Should().NotContain("code=invalid_scope");
        }

        [Fact]
        public void GetAuthorizationError_WithSpecialCharactersInClientId_EncodesInMessage()
        {
            // Arrange
            var errorPageUri = "https://example.com/error";
            var request = new AuthorizeRequest
            {
                ClientId = "client-with-special-chars-&%$",
                RedirectUri = "https://example.com/callback",
                Scope = "openid"
            };
            OIDCClientCredential clientCredential = null;

            // Act
            var result = Helper.GetAuthorizationError(errorPageUri, request, clientCredential);

            // Assert
            result.Should().Contain("client-with-special-chars-&%$");
        }

        [Fact]
        public void GetAuthorizationError_WithMatchingNullValues_ConsidersAsMatching()
        {
            // Arrange
            var errorPageUri = "https://example.com/error";
            var request = new AuthorizeRequest
            {
                ClientId = "client-123",
                RedirectUri = null,
                Scope = null
            };
            var clientCredential = new OIDCClientCredential
            {
                ItemId = "client-123",
                RedirectUri = null,
                Scope = null
            };

            // Act
            var result = Helper.GetAuthorizationError(errorPageUri, request, clientCredential);

            // Assert
            result.Should().Be("https://example.com/error");
        }

        #endregion
    }
}

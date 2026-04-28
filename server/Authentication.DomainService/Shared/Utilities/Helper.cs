using DomainService.Entities;
using DomainService.OAuth.RequestModel;
using HandlebarsDotNet;

namespace DomainService.Utilities
{
    public static class Helper
    {
        public static string LoadAuthorizationHtmlContent(string templateContent, string loginUrl, string apiKey, string username, AuthorizeRequest request, OIDCClientCredential clientCredential)
        {
            var model = new
            {
                response_type = "code",
                client_id = clientCredential.ItemId,
                redirect_uri = clientCredential.RedirectUri ?? "",
                Scope = clientCredential.Scope ?? "",
                State = request.State,
                Nonce = request.Nonce,
                LoginEndpointUrl = loginUrl,
                AcknowledgeUri = loginUrl,
                XBlocksKey = apiKey,
                Username = username,
                user = username
            };

            var template = Handlebars.Compile(templateContent);

            // Render with model values
            return template(model);
        }

        public static string GetAuthorizationError(string errorPageUri, AuthorizeRequest request, OIDCClientCredential clientCredential)
        {
            var validationRules = new (Func<bool> condition, string title, string code, string message)[]
                                  {
                                        (() => clientCredential == null,
                                         "Client not found",
                                         "invalid_client",
                                         $"no client exist with clientId {request.ClientId}"),

                                        (() => clientCredential?.RedirectUri != request.RedirectUri,
                                         "Redirect URI mismatch",
                                         "invalid_request",
                                         $"The redirect_uri {request.RedirectUri} does not match the registered redirect URI."),

                                        (() => clientCredential?.Scope != request.Scope,
                                         "Scope mismatch",
                                         "invalid_scope",
                                         $"The scope {request.Scope} does not match the registered scope.")
                                   };

            var (condition, title, code, message) = validationRules.FirstOrDefault(rule => rule.condition());

            return condition != null
                ? $"{errorPageUri}?title={title}&code={code}&messge={message}":
                errorPageUri;
        }
    }
}

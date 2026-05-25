using Blocks.Genesis;

namespace Blocks.MailDriver;

/// <summary>
/// Defines methods for sending emails using a mail driver service.
/// </summary>
public interface IMailDriverService
{
    /// <summary>
    /// Sends an email to any recipient(s) with the specified request details.
    /// </summary>
    /// <param name="request">The request object containing email details, including recipients, subject, body, and attachments.</param>
    /// <returns>A task that represents the asynchronous operation, containing a response with the status of the email sending process.</returns>
    Task<BaseMutationResponse> SendToAnyAsync(SendMailToAny request);
    /// <summary>
    /// Sends an email to a predefined set of recipients based on a structured template.
    /// </summary>
    /// <param name="request">The request object containing email details, including recipients, subject, body, and attachments.</param>
    /// <returns>A task that represents the asynchronous operation, containing a response with the status of the email sending process.</returns>
    Task<BaseMutationResponse> SendAsync(SendMail request);
    /// <summary>
    /// Retrieves all email templates matching the specified filter criteria.
    /// </summary>
    /// <param name="request">The request object containing filter, pagination, and sorting parameters.</param>
    /// <returns>A task that represents the asynchronous operation, containing a response with the total count and list of matching email templates.</returns>
    Task<GetAllTemplatesResponse> GetAllTemplatesAsync(GetAllTemplates request);
}

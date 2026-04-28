using Blocks.Genesis;

namespace Iam.DomainService.Utilities
{
    public static class Constants
    {
        public const string IamQueue = "blocks_iam_listener";
        public const string AuthenticationQueue = "blocks_authentication_listener";
        public const string MailQueue = "blocks_mail_listener";
        public const string IdentifierQueue = "blocks_identifier_listener";

        public static MessageConfiguration GetMessageConfiguration()
        {
            return new MessageConfiguration
            {
                AzureServiceBusConfiguration = new AzureServiceBusConfiguration
                {
                    Queues = [IamQueue],
                    Topics = []
                }
            };
        
        } 
    }
}

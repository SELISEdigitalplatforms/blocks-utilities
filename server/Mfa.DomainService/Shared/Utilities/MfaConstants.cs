using Blocks.Genesis;

namespace Mfa.DomainService.Utilities
{
    public static class MfaConstants
    {

        public static MessageConfiguration GetMessageConfiguration()
        {
            return new MessageConfiguration
            {
                AzureServiceBusConfiguration = new AzureServiceBusConfiguration
                {
                    Queues = [MfaQueueName],
                    Topics = []
                }
            };
        }

        public const string ApiServiceName = "blocks-mfa-api";
        public const string WorkerServiceName = "blocks-mfa-worker";
        public const string DefaultMfaTemplateName = "MfaViaEmail";
        public const string DefaultMfaTemplateId = "0b121378-3c3d-44f3-a855-9da08cbef48c";      
        public const string MfaQueueName = "blocks_mfa_listener";
        public const string AuthenticationQueue = "blocks_authentication_listener";
    }
}

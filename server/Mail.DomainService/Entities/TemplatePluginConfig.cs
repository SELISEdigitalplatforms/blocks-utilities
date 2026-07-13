using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace Mail.DomainService.Entities
{
    public class TemplatePluginConfig
    {
        [BsonId]
        public string ItemId { get; set; }
        public string PluginProvider { get; set; }
        public string[] RolesAllowedToExecute { get; set; }
        public string MessageId { get; set; }
        public string RequestUri { get; set; }
        public string HttpMethod { get; set; }
        public Dictionary<string, string> HttpHeders { get; set; }
        public string Payload { get; set; }
        public int MaxRetry { get; set; }
        public int DelayInMillisecondsBetweenRetries { get; set; }
        public HttpStatusCode ExpectedStatusCode { get; set; }
        public HttpStatusCode DontRetryIfStatusCode { get; set; }
        public string MessageCoRrelationId { get; set; }
        public string CallbackUri { get; set; }
        public string CallBackQueueName { get; set; }
        public string StateData { get; set; }
        public bool AllowInvalidServerCertificate { get; set; }
        public bool CallBackThroughPushNotification { get; set; }
        public string ContentType { get; set; }
    }
}

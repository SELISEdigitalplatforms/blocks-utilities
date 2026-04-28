using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DomainService.Entities
{
    public class ProjectStatusTracer
    {
        [BsonId]
        public required string ProjectId { get; init; }
        public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
        public bool IsCertificatesUploaded { get; set; }
        public bool IsProjectUpdated { get; set; }
        public string ErrorMessage { get; set; }
        public bool IsProjectCreationSuccess { get; set; }
        public bool IsDefaultConfigurationCopied { get; set; }
        public bool InsertedIntoProjectPeople { get; set; }
    }
}

using MongoDB.Bson.Serialization.Attributes;

namespace DomainService.Dtos
{
    [BsonIgnoreExtraElements]
    public class GetProjectPeople
    {
        [BsonId]
        public string ItemId { get; set; }  
        public string TenantId { get; set; }
        public bool IsInvitationSent { get; set; }
        public bool IsInvitationConfirmed { get; set; }
        public bool IsCreator { get; set; }
        
        public string Enviroment { get; set; }
        public PeopleDetails peopleDetails { get; set; }
    }

    public record  PeopleDetails
    {
        public string? Salutation { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Email { get; set; }
        public string? ProfileImageUrl { get; set; }
        public string UserId { get; set; }
        public bool AllowResendActivation { get; set; }
    }
}

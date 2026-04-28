using Blocks.Genesis;
using DomainService.Dtos;
using MongoDB.Bson.Serialization.Attributes;

namespace DomainService.People
{
    public class GetPeoplesRequest : BaseGetsRequest<string>
    {
        public string ProjectGroupId { get; set; }
        public List<string>? EnvironmentIds { get; set; }
        public bool? IsInvitationConfirmed { get; set; }
    }

    public class GetPeoples
    {
        public PeopleDetails peopleDetails { get; set; }
        public List<SharedEnviroment> SharedEnviroments { get; set; }
    }

    public class GetPeoplesResponse : BaseResponse
    {
        public bool IsOwner { get; set; }
        public List<GetPeoples> Peoples { get; set; }
        public long TotalCount { get; set; }
        public long PeoplesTotalCount { get; set; }
    }

    public class SharedEnviroment
    {
        [BsonId]
        public string ItemId { get; set; }
        public string TenantId { get; set; }
        public bool IsInvitationSent { get; set; }
        public bool IsInvitationConfirmed { get; set; }
        public bool IsCreator { get; set; }
        public string Enviroment { get; set; }
    }
}

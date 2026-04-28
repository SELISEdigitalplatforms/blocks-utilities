using Blocks.Genesis;
using DomainService.Entities;
namespace DomainService.Shared.ResponseModel
{
    public class GetUserCodesByUserIdResponse
    {
        public string ItemId { get; set; }
        public DateTime CreatedDate { get; set; }
        public string Code { get; set; }
        public string UserId { get; set; }
        public string ClientId { get; set; }
        public int? CodeTtlInMinute { get; set; }
        public DateTime? ExpiryDate { get; set; }
        public string Note { get; set; }
    }
}

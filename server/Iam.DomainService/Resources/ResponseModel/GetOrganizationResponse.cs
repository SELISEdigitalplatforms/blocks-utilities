using Blocks.Genesis;
using Iam.DomainService.Shared.Entities;

namespace Iam.DomainService.Resources.ResponseModel
{
    public class GetOrganizationResponse: BaseResponse
    {
        public Organization Organization { get; set; }
    }
}

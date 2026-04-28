using DomainService.People;
using Microsoft.AspNetCore.Mvc;
using Blocks.Genesis;
using Microsoft.AspNetCore.Authorization;

namespace Api.Controllers
{
    [ApiController]
    [Route("[controller]/[action]")]
    public class PeopleController : ControllerBase
    {
        private readonly IPeopleService _peopleService;

        public PeopleController(IPeopleService peopleService)
        {
            _peopleService = peopleService;
        }


        [HttpPost]
        [Authorize]
        public async Task<IActionResult> Invite([FromBody] InviteRequest requests)
        {
            if (requests.Invitations.Count == 0) return BadRequest(new InviteResponse());

            var result = await _peopleService.InvitePeoplesAsync(requests);
            return result.IsSuccess ? Ok(result) : BadRequest(result);
        }


        [HttpPost]
        [Authorize]
        public async Task<IActionResult> RemoveAccess([FromBody] RemoveAccessRequest command)
        {
            var result = await _peopleService.RemoveAccessFromProjectAsync(command);
            return result.IsSuccess ? Ok(result) : BadRequest(result);
        }


        [HttpPost]
        [Authorize]
        public async Task<GetPeoplesResponse> Gets([FromBody] GetPeoplesRequest command)
        {
            return await _peopleService.GetPeoplesAsync(command);
        }

        [HttpPost]
        [Authorize]
        public async Task<IActionResult> ResendInvitation([FromBody] ResendInvitationRequest command)
        {
            var result = await _peopleService.ResendInvitationAsync(command);
            return result.IsSuccess ? Ok(result) : BadRequest(result);
        }

        [HttpPost]
        public async Task<IActionResult> ConfirmInvitation([FromBody] ConfirmInvitationRequest command)
        {
            var result = await _peopleService.ConfirmInvitationAsync(command);
            return result.IsSuccess ? Ok(result) : BadRequest(result);
        }

        [HttpPost]
        public async Task<IActionResult> Signup([FromBody] SignupRequest command)
        {
            var result = await _peopleService.SignupAsync(command);
            return result.IsSuccess ? Ok(result) : BadRequest(result);
        }

        [HttpPost]
        [Authorize]
        public async Task<IActionResult> TransferOwnerShip([FromBody] TransferOwnershipRequest request)
        {
            var result = await _peopleService.TransferOwnershipAsync(request);
            return result.IsSuccess ? Ok(result) : BadRequest(result);
        }
    }
}

using Api.Controllers;
using Blocks.Genesis;
using Moq;
using Utility.DomainService.Sequence;
using Utility.DomainService.Sequence.service;

namespace XUnitTest.Sequence
{
    public class SequenceControllerTests
    {
        private readonly Mock<ISequenceService> _sequenceService = new();
        private readonly SequenceController _controller;

        public SequenceControllerTests()
        {
            _controller = new SequenceController(
                _sequenceService.Object);
        }

        [Fact]
        public async Task Next_Returns_Service_Response()
        {
            var query = new SequenceNumberQuery();
            var response = new SequenceNumberQueryResponse();

            _sequenceService.Setup(s => s.GetNextSequenceNumberAsync(query)).ReturnsAsync(response);

            var result = await _controller.Next(query);

            Assert.Same(response, result);
        }

        [Fact]
        public async Task NextHex_Returns_Service_Response()
        {
            var query = new SequenceNumberHexQuery();
            var response = new SequenceNumberHexQueryResponse();

            _sequenceService.Setup(s => s.GetNextHexSequenceNumberAsync(query)).ReturnsAsync(response);

            var result = await _controller.NextHex(query);

            Assert.Same(response, result);
        }

        [Fact]
        public async Task Reset_Returns_Service_Response()
        {
            var request = new ResetSequenceNumberRequest { Context = "ctx" };
            var response = new BaseResponse();

            _sequenceService.Setup(s => s.ResetSequenceNumberAsync(request)).ReturnsAsync(response);

            var result = await _controller.Reset(request);

            Assert.Same(response, result);
        }
    }
}

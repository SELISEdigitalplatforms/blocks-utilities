using FluentAssertions;
using Utility.DomainService.Geolocation;

namespace XUnitTest.Geolocation
{
    public class GeoRestrictionResponseTests
    {
        [Fact]
        public void CreateNotRestricted_ShouldSetRestrictedFalse()
        {
            var result = GeoRestrictionResponse.CreateNotRestricted();

            result.Restricted.Should().BeFalse();
            result.Reason.Key.Should().BeNull();
            result.Reason.Value.Should().BeNull();
        }

        [Fact]
        public void CreateRestricted_ShouldSetRestrictedTrueAndReason()
        {
            var result = GeoRestrictionResponse.CreateRestricted("forbidden", "country not allowed");

            result.Restricted.Should().BeTrue();
            result.Reason.Key.Should().Be("forbidden");
            result.Reason.Value.Should().Be("country not allowed");
        }
    }
}

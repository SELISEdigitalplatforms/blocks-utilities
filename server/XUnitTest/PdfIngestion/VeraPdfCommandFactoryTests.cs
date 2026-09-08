using FluentAssertions;
using Utility.DomainService.PdfGenerator.Tooling.Options;
using Utility.DomainService.PdfGenerator.Tooling.VeraPdf;

namespace XUnitTest.PdfIngestion;

public sealed class VeraPdfCommandFactoryTests
{
    private readonly VeraPdfCommandFactory _factory = new();

    [Fact]
    public void Direct_validate_requests_xml_output()
    {
        var command = _factory.CreateValidateCommand("/work/input.pdf", new PdfToolingOptions());

        command.FileName.Should().Be("/opt/verapdf/verapdf");
        command.Arguments.Should().Equal("--format", "xml", "/work/input.pdf");
    }

    [Fact]
    public void Docker_validate_mounts_the_workspace_and_still_requests_xml()
    {
        var options = new PdfToolingOptions
        {
            VeraPdfExecutionMode = VeraPdfExecutionMode.Docker,
            VeraPdfDockerImage = "ghcr.io/verapdf/cli:latest"
        };

        var workspaceDir = Path.Combine(Path.GetTempPath(), "op-1");
        var inputPath = Path.Combine(workspaceDir, "input.pdf");

        var command = _factory.CreateValidateCommand(inputPath, options);

        command.FileName.Should().Be("docker");
        command.Arguments.Should().ContainInOrder("-v", $"{workspaceDir}:/work", "ghcr.io/verapdf/cli:latest", "--format", "xml", "/work/input.pdf");
    }

    [Fact]
    public void Direct_version_command_is_just_version()
    {
        var command = _factory.CreateVersionCommand(new PdfToolingOptions());

        command.FileName.Should().Be("/opt/verapdf/verapdf");
        command.Arguments.Should().Equal("--version");
    }
}

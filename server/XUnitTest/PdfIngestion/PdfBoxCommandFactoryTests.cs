using FluentAssertions;
using Utility.DomainService.PdfGenerator.Tooling.Options;
using Utility.DomainService.PdfGenerator.Tooling.PdfBox;

namespace XUnitTest.PdfIngestion;

public sealed class PdfBoxCommandFactoryTests
{
    private readonly PdfBoxCommandFactory _factory = new();

    [Fact]
    public void Direct_flatten_runs_the_flatten_main_class_with_the_configured_classpath()
    {
        var options = new PdfToolingOptions
        {
            JavaPath = "/usr/bin/java",
            PdfBoxJarPath = "/opt/pdfbox/pdfbox-app.jar",
            PdfBoxClassPath = "/opt/pdfbox"
        };

        var command = _factory.CreateFlattenCommand("/work/input.pdf", "/work/output.pdf", 60, options);

        command.FileName.Should().Be("/usr/bin/java");
        command.Arguments.Should().ContainInOrder(
            "-cp", $"/opt/pdfbox/pdfbox-app.jar{Path.PathSeparator}/opt/pdfbox",
            PdfBoxCommandFactory.FlattenMainClass,
            "/work/input.pdf", "/work/output.pdf");
    }

    [Fact]
    public void Direct_normalize_geometry_runs_the_geometry_main_class()
    {
        var command = _factory.CreateNormalizeGeometryCommand("/work/input.pdf", "/work/output.pdf", 60, new PdfToolingOptions());

        command.Arguments.Should().Contain(PdfBoxCommandFactory.NormalizeGeometryMainClass);
    }

    [Fact]
    public void Direct_standardize_passes_the_standardize_flag_to_the_flatten_class()
    {
        // There is no separate "standardize" main class - PdfBoxFlatten itself handles
        // --standardize, per tools/pdfbox/PdfBoxFlatten.java.
        var command = _factory.CreateStandardizeCommand("/work/input.pdf", "/work/output.pdf", 60, new PdfToolingOptions());

        command.Arguments.Should().ContainInOrder(PdfBoxCommandFactory.FlattenMainClass, "--standardize", "/work/input.pdf", "/work/output.pdf");
    }

    [Fact]
    public void Docker_flatten_mounts_the_workspace_and_uses_container_paths()
    {
        var options = new PdfToolingOptions
        {
            PdfBoxExecutionMode = PdfBoxExecutionMode.Docker,
            PdfBoxDockerImage = "blocks-pdfbox:dev"
        };

        var workspaceDir = Path.Combine(Path.GetTempPath(), "op-1");
        var inputPath = Path.Combine(workspaceDir, "input.pdf");
        var outputPath = Path.Combine(workspaceDir, "output.pdf");

        var command = _factory.CreateFlattenCommand(inputPath, outputPath, 60, options);

        command.FileName.Should().Be("docker");
        command.Arguments.Should().ContainInOrder(
            "-v", $"{workspaceDir}:/work", "blocks-pdfbox:dev",
            PdfBoxCommandFactory.FlattenMainClass, "/work/input.pdf", "/work/output.pdf");
    }

    [Fact]
    public void Docker_flatten_refuses_input_and_output_in_different_workspaces()
    {
        var options = new PdfToolingOptions { PdfBoxExecutionMode = PdfBoxExecutionMode.Docker };

        var act = () => _factory.CreateFlattenCommand("/host/op-1/input.pdf", "/host/op-2/output.pdf", 60, options);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Docker_standardize_refuses_input_and_output_in_different_workspaces()
    {
        var options = new PdfToolingOptions { PdfBoxExecutionMode = PdfBoxExecutionMode.Docker };

        var act = () => _factory.CreateStandardizeCommand("/host/op-1/input.pdf", "/host/op-2/output.pdf", 60, options);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Direct_version_command_asks_the_flatten_class_for_its_own_version()
    {
        var command = _factory.CreateVersionCommand(new PdfToolingOptions());

        command.Arguments.Should().Contain(PdfBoxCommandFactory.FlattenMainClass);
        command.Arguments.Should().Contain("--version");
    }
}

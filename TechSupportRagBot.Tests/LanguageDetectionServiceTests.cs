using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TechSupportRagBot.Services;

namespace TechSupportRagBot.Tests;

public sealed class LanguageDetectionServiceTests
{
    [Theory]
    [InlineData("Здравствуйте! Как настроить давление на станке?", "ru")]
    [InlineData("Hello, how can I configure the machine pressure?", "en")]
    [InlineData("Bonjour, comment régler la pression de la machine ?", "fr")]
    [InlineData("Hola, ¿cómo puedo ajustar la presión de la máquina?", "es")]
    public void BundledFastTextModelDetectsSupportMessageLanguage(string message, string expectedCode)
    {
        var projectRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "../../../../TechSupportRagBot"));
        Assert.True(File.Exists(Path.Combine(projectRoot, "Resources/fasttext/lid.176.ftz")));
        var environment = new TestEnvironment { ContentRootPath = projectRoot };
        using var service = new LanguageDetectionService(
            environment,
            Options.Create(new FastTextLanguageOptions()),
            NullLogger<LanguageDetectionService>.Instance);

        Assert.Equal(expectedCode, service.DetectLanguageCode(message));
    }

    private sealed class TestEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "TechSupportRagBot.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}

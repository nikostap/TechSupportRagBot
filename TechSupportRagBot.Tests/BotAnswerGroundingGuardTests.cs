using TechSupportRagBot.Services;

namespace TechSupportRagBot.Tests;

public class BotAnswerGroundingGuardTests
{
    private const string Evidence = "Проверьте индикацию датчика 87. Протрите оптику. "
        + "Настройте датчик регулировочным винтом. Удостоверьтесь, что датчик не сломан и разъём не повреждён.";

    [Fact]
    public void RejectsInventedVoltage()
    {
        Assert.True(BotAnswerGroundingGuard.HasUnsupportedClaims(
            "Проверьте питание датчика: должно быть 24 В.", Evidence));
    }

    [Fact]
    public void RejectsUnrelatedProcedureWithoutNumbers()
    {
        Assert.True(BotAnswerGroundingGuard.HasUnsupportedClaims(
            "Проверьте предохранители и защиту цепи в шкафу управления.", Evidence));
    }

    [Fact]
    public void AllowsDocumentedProcedureAndMeasurement()
    {
        const string source = "Настройте реле давления на 2,5 бар. Проверьте контакты реле давления.";
        Assert.False(BotAnswerGroundingGuard.HasUnsupportedClaims(
            "1. Настройте реле давления на 2,5 бар.\n2. Проверьте контакты реле давления.", source));
    }

    [Fact]
    public void FollowUpFallbackQuotesOnlyRelevantSourceSentences()
    {
        const string source = "Датчик 87 настраивается на шпулю. Проверьте что на датчике горит индикация. "
            + "Протрите оптику и поверните датчик. Удостоверьтесь, что датчик не сломан и его разъём не повреждён.";
        var excerpt = BotEvidenceExcerptSelector.Select(source, "На нём лампочка не горит",
            "Проверьте индикацию датчика и разъём. Проверьте предохранители.");

        Assert.NotNull(excerpt);
        Assert.Contains("индикация", excerpt);
        Assert.DoesNotContain("предохранители", excerpt);
    }
}

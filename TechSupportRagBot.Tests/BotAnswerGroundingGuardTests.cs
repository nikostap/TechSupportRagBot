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
    public void RejectsPartlyOverlappingButUnsupportedElectricalChecks()
    {
        Assert.True(BotAnswerGroundingGuard.HasUnsupportedClaims(
            "Осмотрите разъём и кабель датчика на предмет повреждений. Если индикации нет, потребуется замена датчика.",
            Evidence));
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
        var excerpt = BotEvidenceExcerptSelector.Select(source, "Где горит лампочка",
            "Проверьте индикацию датчика и разъём. Проверьте предохранители.");

        Assert.NotNull(excerpt);
        Assert.Contains("индикация", excerpt);
        Assert.DoesNotContain("предохранители", excerpt);
    }

    [Fact]
    public void UnlitLampSelectsIndicationInsteadOfUnrelatedAdjustments()
    {
        const string source = "Датчик 87 настраивается на шпулю. Проверьте что на датчике горит индикация. "
            + "Поверните датчик так, чтобы световое пятно было направлено на шпулю. "
            + "На датчике есть регулировочный винт: в меню ВХОДЫ / ВЫХОДЫ найдите индикатор 87.";
        var excerpt = BotEvidenceExcerptSelector.Select(source, "Где светится лампочка на датчике",
            "Поверните датчик и проверьте индикатор, питание и разъём.");

        Assert.Equal("Проверьте что на датчике горит индикация.", excerpt);
        var verboseDraft = "В меню ВХОДЫ / ВЫХОДЫ найдите индикатор 87, когда шпуля есть, "
            + "индикатор должен быть оранжевым, а регулировочный винт на датчике настройте.";
        Assert.Equal("Проверьте что на датчике горит индикация.",
            BotEvidenceExcerptSelector.Select(source, "Где светится лампочка на датчике", verboseDraft));
        Assert.Null(BotEvidenceExcerptSelector.Select(source, "Не светится лампочка на датчике", verboseDraft));
    }

    [Fact]
    public void PlainFollowUpDoesNotRepeatImagesButLocationRequestMayUseThem()
    {
        var plan = new BotConversationPlan("", "", true, "", false, false);
        Assert.False(BotFollowUpMediaPolicy.ShouldAttach("Лампочка на датчике не горит", plan));
        Assert.True(BotFollowUpMediaPolicy.ShouldAttach("Где находится датчик?", plan));
    }

    [Fact]
    public void ShortAmbiguousQuestionAsksBetweenEquallyRankedQaIssues()
    {
        var chunks = new List<RagChunkCandidate>
        {
            new() { DocumentType = "QA", QAStatus = "Verified", Title = "Рычаги не захватывают шпулю?", RerankScore = 0.881 },
            new() { DocumentType = "QA", QAStatus = "Verified", Title = "Нет шпули в бункере?", RerankScore = 0.878 }
        };

        Assert.NotNull(BotQaAmbiguity.GetChoices("Станок не замечает шпулю", chunks, false));
        Assert.Null(BotQaAmbiguity.GetChoices("На датчике 87 нет индикации", chunks, false));
        Assert.Null(BotQaAmbiguity.GetChoices("Станок не замечает шпулю", chunks, true));
    }
}

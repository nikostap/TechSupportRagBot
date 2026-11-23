using TechSupportRagBot.Services;

namespace TechSupportRagBot.Tests;

public class BotAnswerGroundingGuardTests
{
    [Fact]
    public void SourceConditionCannotBecomeAnUnreportedCustomerFact()
    {
        const string title = "После замены нити она остаётся холодной?";
        const string report = "Не работает нить отрезки. Становится холодная";
        Assert.True(BotAnswerGroundingGuard.AttributesUnreportedCondition(
            "Понял, нить остаётся холодной после замены.", title, report));
        Assert.False(BotAnswerGroundingGuard.AttributesUnreportedCondition(
            "Если это происходит после замены, проверьте кабели.", title, report));
        Assert.False(BotAnswerGroundingGuard.AttributesUnreportedCondition(
            "Нить остаётся холодной после замены.", title, "После замены нить холодная"));
        Assert.True(BotAnswerGroundingGuard.AttributesUnreportedCondition(
            "Лампа гаснет через час.", "Лампа гаснет через час работы?", "Лампа гаснет"));
    }

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
    public void SensorNumberSelectsLiteralSourceSentenceForLocationFollowUp()
    {
        const string source = "Датчик 87 настраивается на последнюю шпулю. Протрите оптику датчика.";
        Assert.Equal("Датчик 87 настраивается на последнюю шпулю.",
            BotEvidenceExcerptSelector.Select(source, "Где датчик 87?", "Проверьте датчик 87 в бункере."));
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

    [Fact]
    public void ExactIssuePhrasePreventsFalseAmbiguity()
    {
        var chunks = new List<RagChunkCandidate>
        {
            new() { DocumentType = "QA", QAStatus = "Verified", Title = "Ошибка нет шпули в бункере?", RerankScore = 0.882 },
            new() { DocumentType = "QA", QAStatus = "Verified", Title = "При подаче шпули ошибка датчиков 85 и 81?", RerankScore = 0.881 }
        };
        Assert.Null(BotQaAmbiguity.GetChoices("Пишет нет шпули", chunks, false));
        Assert.Null(BotQaAmbiguity.GetChoices("станок пишет, что нет шпули", chunks, false));
        Assert.Null(BotQaAmbiguity.GetChoices("А теперь ошибка нет шпули", chunks, false));
    }

    [Theory]
    [InlineData("Становится холодная", 250)]
    [InlineData("Нить холодная", 250)]
    [InlineData("После двух рулонов", 249)]
    [InlineData("После замены", 250)]
    public void LatestDetailNarrowsEvidenceForTheCurrentIssue(string reply, int expectedChunkId)
    {
        var chunks = new List<RagChunkCandidate>
        {
            new() { ChunkId = 250, QAEntryId = 185, DocumentType = "QA", QAStatus = "Verified",
                Title = "После замены нити отрезки она остаётся холодной и появляется ошибка нагрева?",
                Solution = "Проверьте гибкие силовые кабели к подвижному узлу нити отрезки." },
            new() { ChunkId = 249, QAEntryId = 197, DocumentType = "QA", QAStatus = "Verified",
                Title = "После двух готовых рулонов станок останавливается с предупреждением о нити отрезки?",
                Solution = "Проверьте подключение проводов нити отрезки." },
            new() { ChunkId = 1760, QAEntryId = 231, DocumentType = "QA", QAStatus = "Verified",
                Title = "Ошибка низкое давление?", Solution = "Проверьте давление." }
        };
        Assert.Equal(expectedChunkId, BotFollowUpFocus.SelectByLatestDetail(chunks,
            "Ошибка не работает нитка отрезки", reply)?.ChunkId);
        Assert.Null(BotFollowUpFocus.SelectByLatestDetail(chunks,
            "Ошибка не работает нитка отрезки", "Нить отрезки"));
        Assert.Null(BotFollowUpFocus.SelectByLatestDetail(chunks,
            "Ошибка нет шпули", "Становится холодная"));
    }

    [Theory]
    [InlineData("Не загорается", 41)]
    [InlineData("Через час", 42)]
    [InlineData("При запуске", 41)]
    public void LatestDetailNarrowsEvidenceAcrossDifferentFaults(string reply, int expectedChunkId)
    {
        var chunks = new List<RagChunkCandidate>
        {
            new() { ChunkId = 41, QAEntryId = 31, DocumentType = "QA", QAStatus = "Verified",
                Title = "Лампа не загорается при запуске?", Solution = "Проверьте питание лампы." },
            new() { ChunkId = 42, QAEntryId = 32, DocumentType = "QA", QAStatus = "Verified",
                Title = "Лампа гаснет через час работы?", Solution = "Проверьте охлаждение лампы." },
            new() { ChunkId = 43, QAEntryId = 33, DocumentType = "QA", QAStatus = "Verified",
                Title = "Давление воздуха упало?", Solution = "Проверьте воздух." }
        };
        Assert.Equal(expectedChunkId, BotFollowUpFocus.SelectByLatestDetail(chunks,
            "Лампа работает неправильно", reply)?.ChunkId);
        Assert.Null(BotFollowUpFocus.SelectByLatestDetail(chunks,
            "Лампа работает неправильно", "Лампа"));
    }

    [Fact]
    public void LatestDetailExcludesAnUnrelatedSensorEvenWithOnlyOneSourceOnIssue()
    {
        var chunks = new List<RagChunkCandidate>
        {
            new() { ChunkId = 1759, Title = "Ошибка нет шпули в бункере?", Solution = "Проверьте датчик 87." },
            new() { ChunkId = 229, Title = "Ошибка датчика 85", NodeName = "Бункер",
                Solution = "Проверьте датчик 85 под бункером, он видит шпулю." }
        };
        Assert.Equal(1759, BotFollowUpFocus.SelectByLatestDetail(chunks,
            "Ошибка нет шпули", "В бункере")?.ChunkId);
    }

    [Fact]
    public void NegationSelectsMissingSpoolQaOverNearbySpoolSensorQa()
    {
        var chunks = new List<RagChunkCandidate>
        {
            new() { DocumentType = "QA", QAStatus = "Verified", QAEntryId = 1,
                Title = "При подаче шпули появляются ошибки датчиков 85 и 81?", RerankScore = 0.9 },
            new() { DocumentType = "QA", QAStatus = "Verified", QAEntryId = 2,
                Title = "Ошибка нет шпули в бункере?", RerankScore = 0.8 }
        };

        Assert.Equal(2, BotAnswerMediaSelector.SelectQaEntryId("нет шпули", chunks));
    }
}

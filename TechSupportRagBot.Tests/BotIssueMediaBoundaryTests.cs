using TechSupportRagBot.Services;

namespace TechSupportRagBot.Tests;

public sealed class BotIssueMediaBoundaryTests
{
    [Fact]
    public async Task SeparateFaultReportsDoNotInheritEarlierTroubleshooting()
    {
        const string first = "Клиент: ошибка низкого давления\nБот: Проверьте реле давления.";
        Assert.True(BotIssueBoundary.StartsDifferentFault("ошибка датчика 87", first));
        Assert.Equal(BotTurnIntent.NewQuestion,
            (await BotTurnClassifier.ClassifyAsync("ошибка датчика 87", first, null!, CancellationToken.None)).Intent);
        var plan = BotConversationPlanner.Create("ошибка датчика 87", first);
        Assert.False(plan.IsFollowUp);
        Assert.Equal("ошибка датчика 87", plan.SearchQuestion);

        const string second = first + "\nКлиент: ошибка датчика 87\nБот: Проверьте датчик 87.";
        Assert.True(BotIssueBoundary.StartsDifferentFault("Ошибка нажат аварийный стоп", second));
        Assert.Equal(BotTurnIntent.NewQuestion,
            (await BotTurnClassifier.ClassifyAsync("Ошибка нажат аварийный стоп", second, null!, CancellationToken.None)).Intent);
        Assert.False(BotConversationPlanner.Create("Ошибка нажат аварийный стоп", second).IsFollowUp);
    }

    [Fact]
    public void FaultCodeClarificationStaysWithExistingIssue()
    {
        const string history = "Клиент: Ошибка сервопривода\nБот: Какой код ошибки?";
        Assert.False(BotIssueBoundary.StartsDifferentFault("Ошибка E34", history));
        Assert.True(BotConversationPlanner.Create("Ошибка E34", history).IsFollowUp);
    }

    [Fact]
    public async Task NewMachineClarificationUsesLatestFaultInsteadOfEarlierPressureFault()
    {
        const string history = "Клиент: ошибка низкого давления\nБот: Проверьте датчик 91.\n"
            + "Клиент: а теперь у меня ошибка нет шпули\nБот: Проверьте датчик 87.";
        const string pressureHistory = "Клиент: ошибка низкого давления\nБот: Проверьте датчик 91.";
        Assert.True(BotIssueBoundary.StartsDifferentFault("а теперь у меня ошибка нет шпули", pressureHistory));
        Assert.Equal(BotTurnIntent.NewQuestion,
            (await BotTurnClassifier.ClassifyAsync("а теперь у меня ошибка нет шпули", pressureHistory,
                null!, CancellationToken.None)).Intent);
        var plan = BotConversationPlanner.Create("у меня новый станок", history, true,
            BotTurnIntent.Clarification);
        Assert.True(plan.IsFollowUp);
        Assert.Equal("а теперь у меня ошибка нет шпули", plan.OriginalProblem);
        Assert.Contains("ошибка нет шпули", plan.SearchQuestion);
        Assert.DoesNotContain("низкого давления", plan.SearchQuestion);
    }

    [Fact]
    public void ExactSpoolQaCanBeReturnedWithoutMixingOtherChunks()
    {
        var spool = Qa(87, "Ошибка нет шпули в бункере?", 1.72);
        spool.Solution = "Проверьте датчик 87.";
        var pressure = Qa(91, "Ошибка - низкое давление пневмосистемы?", 0.82);
        pressure.Solution = "Проверьте датчик 91.";
        Assert.Same(spool, BotExactQaSelector.Select("ошибка нет шпули", [spool, pressure]));
        Assert.Same(spool, BotExactQaSelector.Select("а теперь у меня ошибка нет шпули", [spool, pressure]));
        Assert.Null(BotExactQaSelector.Select("ошибка датчика", [spool, pressure]));
    }

    [Fact]
    public void MediaComesOnlyFromQaMatchingCurrentQuestion()
    {
        var chunks = new[]
        {
            Qa(91, "Ошибка - низкое давление пневмосистемы?", 10),
            Qa(87, "Ошибка датчика 87: нет шпули в бункере", 5),
            Qa(1, "Ошибка Нажат аварийный стоп?", 4)
        };
        Assert.Equal(91, BotAnswerMediaSelector.SelectQaEntryId("ошибка низкого давления", chunks));
        Assert.Equal(87, BotAnswerMediaSelector.SelectQaEntryId("ошибка датчика 87", chunks));
        Assert.Equal(1, BotAnswerMediaSelector.SelectQaEntryId("Ошибка нажат аварийный стоп", chunks));
        Assert.Null(BotAnswerMediaSelector.SelectQaEntryId("ошибка датчика 87", [chunks[0], chunks[2]]));
        Assert.Null(BotAnswerMediaSelector.SelectQaEntryId("неизвестная тема", chunks));
    }

    private static RagChunkCandidate Qa(int id, string title, double score) => new()
    {
        QAEntryId = id, QAStatus = "Verified", DocumentType = "QA",
        Title = title, RerankScore = score
    };
}

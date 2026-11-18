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

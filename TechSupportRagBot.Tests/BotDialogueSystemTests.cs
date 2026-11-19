using TechSupportRagBot.Models;
using TechSupportRagBot.Services;

namespace TechSupportRagBot.Tests;

public sealed class BotDialogueSystemTests
{
    [Theory]
    [InlineData("А еще пишет низкое давление")]
    [InlineData("А теперь аварийный стоп горит")]
    [InlineData("Теперь ошибка правого кожуха")]
    public async Task ExplicitAdditionalFaultStartsAFocusedSearch(string question)
    {
        const string history = "Клиент: Не видит шпулю\nБот: Проверьте датчик бункера.";

        Assert.True(BotIssueBoundary.StartsDifferentFault(question, history));
        Assert.Equal(BotTurnIntent.NewQuestion,
            (await BotTurnClassifier.ClassifyAsync(question, history, null!, CancellationToken.None)).Intent);
        var plan = BotConversationPlanner.Create(question, history);
        Assert.False(plan.IsFollowUp);
        Assert.Equal(question, plan.SearchQuestion);
    }

    [Theory]
    [InlineData("Серый")]
    [InlineData("Оранжевый")]
    public async Task SpokenChoiceAnswersLastBotQuestion(string answer)
    {
        const string history = "Клиент: Не видит шпулю\nБот: Индикатор серый или оранжевый?";

        var intent = await BotTurnClassifier.ClassifyAsync(answer, history, null!, CancellationToken.None);
        Assert.Equal(BotTurnIntent.Clarification, intent.Intent);
        var plan = BotConversationPlanner.Create(answer, history, interpretedIntent: intent.Intent);
        Assert.True(plan.IsFollowUp);
        Assert.False(plan.NeedsChoiceClarification);
        Assert.Contains(answer, plan.SearchQuestion);
    }

    [Fact]
    public void LastQuestionDoesNotBorrowEarlierAlternatives()
    {
        const string history = "Клиент: Не видит шпулю\n"
            + "Бот: Датчик смотрит на последнюю или предпоследнюю шпулю. Чем могу помочь ещё?";

        var plan = BotConversationPlanner.Create("Да", history);
        Assert.False(plan.NeedsChoiceClarification);
        Assert.True(plan.NeedsDetailClarification);
    }

    [Fact]
    public void RejectedCauseDoesNotRemainInFocusedRetrievalQuestion()
    {
        const string history = "Клиент: Не видит шпулю и низкое давление\n"
            + "Бот: Проверьте оба узла.";

        var plan = BotConversationPlanner.Create("Нет, давление нормальное, только шпулю не видит",
            history, interpretedIntent: BotTurnIntent.Negation);
        Assert.True(plan.IsFollowUp);
        Assert.Equal("шпулю не видит", plan.SearchQuestion);
        Assert.True(plan.IsCorrection);
    }

    [Fact]
    public void DetailAfterRejectedCauseStaysOnTheRemainingIssue()
    {
        const string history = "Клиент: Не видит шпулю и низкое давление\n"
            + "Бот: Проверьте оба узла.\n"
            + "Клиент: Нет, давление нормальное, только шпулю не видит\n"
            + "Бот: Проверьте датчик шпули.";

        var plan = BotConversationPlanner.Create("Его индикатор не светится", history,
            interpretedIntent: BotTurnIntent.Clarification);
        Assert.True(plan.IsFollowUp);
        Assert.StartsWith("шпулю не видит", plan.SearchQuestion);
        Assert.DoesNotContain("давление", plan.SearchQuestion);
    }

    [Theory]
    [InlineData("Станок не работает", "Пишет нет шпули")]
    [InlineData("Машина сломалась", "Ролик не крутится")]
    public void SpecificDetailReplacesVagueOpening(string opening, string detail)
    {
        var history = $"Клиент: {opening}\nБот: Что именно происходит?";
        var plan = BotConversationPlanner.Create(detail, history, interpretedIntent: BotTurnIntent.Clarification);
        Assert.True(plan.IsFollowUp);
        Assert.Equal(detail, plan.OriginalProblem);
        Assert.DoesNotContain(opening, plan.SearchQuestion);
    }

    [Fact]
    public void SelectedMachineDesignationIsNotAFaultCode()
    {
        var machine = new Machine { Model = "L", Name = "L", SerialNumber = "800" };

        Assert.Null(BotTechnicalCode.Extract("Ошибка низкого давления на L-800", null, machine));
        Assert.Equal("E34", BotTechnicalCode.Extract("Ошибка E34 на L-800", null, machine));
        Assert.Equal("OC", BotTechnicalCode.Extract("Пишет OC, мотор не крутится", null, machine));
    }

    [Fact]
    public void VagueFirstReportAsksForAnObservableSymptom()
    {
        Assert.True(BotQuestionSpecificity.NeedsSymptom("Станок не работает"));
        Assert.False(BotQuestionSpecificity.NeedsSymptom("Пишет нет шпули"));
        Assert.False(BotQuestionSpecificity.NeedsSymptom("Ошибка низкого давления"));
        Assert.False(BotQuestionSpecificity.NeedsSymptom("Датчик 87 не работает"));
    }

    [Theory]
    [InlineData("BotTraceNeedSymptom")]
    [InlineData("BotNeedSpecificSymptom")]
    public void NewMessagesExistInBothLanguages(string key)
    {
        Assert.NotEqual(key, UiText.ForLanguage("ru", key));
        Assert.NotEqual(key, UiText.ForLanguage("en", key));
    }

    [Fact]
    public void DeduplicationKeepsAnInstructionWhenAllStepsWouldDisappear()
    {
        const string previous = "1. Проверьте разъём. 2. Проверьте питание.";
        const string answer = "Вот что нужно проверить:\n1. Проверьте разъём.\n2. Проверьте питание.\n"
            + "Если после этих действий ошибка остаётся, сообщите оператору.";

        Assert.Equal(answer, BotResponseDeduplicator.RemoveRepeatedSteps(answer, previous));
    }

    [Fact]
    public void TwoNamedFaultsCanEachSupplyTheirOwnImage()
    {
        RagChunkCandidate Qa(int id, string title, double score) => new()
        {
            QAEntryId = id, QAStatus = "Verified", DocumentType = "QA",
            Title = title, RerankScore = score
        };
        var chunks = new[]
        {
            Qa(1, "Ошибка нет шпули в бункере", 0.9),
            Qa(2, "Ошибка низкое давление", 1.0),
            Qa(3, "Ошибка при подаче шпули", 1.1)
        };

        Assert.Equal([1, 2], BotAnswerMediaSelector.SelectQaEntryIds(
            "Пишет нет шпули и низкое давление", chunks));
        Assert.Single(BotAnswerMediaSelector.SelectQaEntryIds("Пишет нет шпули", chunks));
        Assert.Single(BotAnswerMediaSelector.SelectQaEntryIds("Ошибка датчиков 85 и 81",
            [Qa(4, "Ошибка датчиков 85 и 81", 1.0), Qa(5, "Ошибка датчика 85", 0.8)]));
    }

    [Fact]
    public void SensorNumberCanMatchAnAliasWhenQaTitleUsesPlainLanguage()
    {
        var spool = new RagChunkCandidate
        {
            QAEntryId = 87, QAStatus = "Verified", DocumentType = "QA",
            Title = "Ошибка нет шпули в бункере?",
            SearchQuestions = "ошибка датчика 87; где находится датчик 87",
            RerankScore = 0.9
        };
        Assert.Equal(87, BotAnswerMediaSelector.SelectQaEntryId("Нет, 87 вообще не светится", [spool]));
    }
}

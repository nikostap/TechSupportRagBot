using TechSupportRagBot.Services;

namespace TechSupportRagBot.Tests;

public class BotConversationPlannerTests
{
    [Theory]
    [InlineData("Датчик 3", "ALF не запускает подачу", "датчик 3")]
    [InlineData("Да", "ALF не запускает подачу", "да")]
    [InlineData("Как проверить его?", "ALF не запускает подачу", "как проверить его")]
    public void AlfFollowUpKeepsTheOriginalProblemInRagQuery(string reply, string problem, string expected)
    {
        var history = $"Клиент: {problem}\nБот: Укажите номер датчика?";

        var plan = BotConversationPlanner.Create(reply, history);

        Assert.True(plan.IsFollowUp);
        Assert.Contains(problem, plan.SearchQuestion);
        Assert.Contains(expected, plan.SearchQuestion, StringComparison.OrdinalIgnoreCase);
        Assert.True(plan.SearchQuestion.Length <= 430);
    }

    [Fact]
    public void AlfSecondClarificationStillUsesOriginalProblem()
    {
        const string history = "Клиент: На ALF не запускается подача этикетки после включения.\n"
            + "Бот: Уточните номер датчика?\nКлиент: Датчик 3\nБот: Что видно на экране?";

        var plan = BotConversationPlanner.Create("Ошибка E34", history);

        Assert.True(plan.IsFollowUp);
        Assert.Contains("не запускается подача", plan.SearchQuestion);
        Assert.Contains("Ошибка E34", plan.SearchQuestion);
    }

    [Theory]
    [InlineData("Тормозная не работает", "муфта", "она не работает")]
    [InlineData("ALF не подаёт этикетку", "датчик S04", "индикатор не горит")]
    [InlineData("На ALF течёт клей", "насос", "течёт после остановки")]
    public void MultipleClarificationsRetainOriginalIssueAndIntermediateDetails(string issue, string detail, string latest)
    {
        var history = $"Клиент: {issue}\nБот: Какой именно узел?\nКлиент: {detail}\nБот: Что с ним происходит?";

        var plan = BotConversationPlanner.Create(latest, history);

        Assert.True(plan.IsFollowUp);
        Assert.Contains(issue, plan.SearchQuestion);
        Assert.Contains(detail, plan.SearchQuestion);
        Assert.Contains(latest, plan.SearchQuestion);
        Assert.True(plan.SearchQuestion.Length <= 440);
    }

    [Fact]
    public void ShortSymptomContinuesDialogueEvenWhenPreviousAnswerDidNotAskAQuestion()
    {
        const string history = "Клиент: ALF не подаёт этикетку\nБот: Проверьте датчик.\n"
            + "Клиент: датчик S04\nБот: Проверьте положение датчика S04.";

        var plan = BotConversationPlanner.Create("индикатор не горит", history);

        Assert.True(plan.IsFollowUp);
        Assert.Contains("датчик S04", plan.SearchQuestion);
        Assert.Contains("индикатор не горит", plan.SearchQuestion);
    }

    [Fact]
    public void NewAlfQuestionDoesNotInheritPreviousIssue()
    {
        const string history = "Клиент: На ALF не запускается подача этикетки.\nБот: Уточните номер датчика?";

        var plan = BotConversationPlanner.Create("Как настроить пароль для ALF?", history);

        Assert.False(plan.IsFollowUp);
        Assert.DoesNotContain("подача", plan.SearchQuestion);
    }

    [Fact]
    public void AlfFoilQuestionAfterSensorDialogueStartsNewSearch()
    {
        const string history = "Клиент: Как настроить датчик S03 на ALF?\nБот: Проверить питание или сигнал?";

        var plan = BotConversationPlanner.Create("Какое рабочее натяжение установить для фольги на ALF?", history);

        Assert.False(plan.IsFollowUp);
        Assert.Equal("Какое рабочее натяжение установить для фольги на ALF?", plan.SearchQuestion);
    }

    [Fact]
    public void AlfMassQuestionIsFollowUpBecauseItSharesTheLoadTopic()
    {
        const string history = "Клиент: Как откалибровать тензовал по эталонному грузу на ALF?\nБот: Используйте груз не менее 16 кг. Чем помочь ещё?";

        var plan = BotConversationPlanner.Create("Какую массу груза использовать?", history);

        Assert.True(plan.IsFollowUp);
        Assert.Contains("тензовал", plan.SearchQuestion);
    }

    [Fact]
    public void AmbiguousYesRequiresChoiceInsteadOfRepeatingInstructions()
    {
        const string history = "Клиент: Как настроить датчик S03 на ALF?\nБот: Проверить питание или сигнал?";

        var plan = BotConversationPlanner.Create("Да", history);

        Assert.True(plan.NeedsChoiceClarification);
    }

    [Fact]
    public void YesToAdditionalSymptomsQuestionRequestsDetails()
    {
        const string history = "Клиент: Как настроить датчик S03 на ALF?\nБот: Есть дополнительные признаки неисправности?";

        var plan = BotConversationPlanner.Create("Да", history);

        Assert.False(plan.NeedsChoiceClarification);
        Assert.True(plan.NeedsDetailClarification);
    }

    [Fact]
    public void AnswerAfterChoiceStillSearchesForTheAlfProblem()
    {
        const string history = "Клиент: Как настроить датчик S03 на ALF?\nБот: Проверить питание или сигнал?\n"
            + "Клиент: Да\nБот: Какой из предложенных вариантов вы имеете в виду?";

        var plan = BotConversationPlanner.Create("Выходной сигнал датчика", history);

        Assert.True(plan.IsFollowUp);
        Assert.Contains("S03", plan.SearchQuestion);
        Assert.Contains("Выходной сигнал", plan.SearchQuestion);
    }

    [Fact]
    public void RepeatedStepsAreRemovedFromAlfFollowUp()
    {
        const string previous = "Для настройки: 1. Нажмите Аварийный стоп. 2. Поверните датчик S03 до выключения индикатора.";
        const string answer = "Для проверки датчика:\n1. Нажмите Аварийный стоп.\n2. Поверните датчик S03 до выключения индикатора.\n3. Проверьте, что индикатор включился.";

        var result = BotResponseDeduplicator.RemoveRepeatedSteps(answer, previous);

        Assert.DoesNotContain("Нажмите Аварийный стоп", result);
        Assert.DoesNotContain("Поверните датчик", result);
        Assert.Contains("1. Проверьте, что индикатор включился", result);
    }

    [Fact]
    public void FlattenedAlfHistoryDoesNotRepeatTheLastStepOrParagraph()
    {
        const string previous = "1. Нажмите Аварийный стоп. 2. После загорания индикатора и остановки привода зафиксируйте кольцо датчика. Если индикатор не гаснет при повороте датчика, проверьте механическое положение кулачка.";
        const string answer = "1. После загорания индикатора и остановки привода зафиксируйте кольцо датчика.\nЕсли индикатор не гаснет при повороте датчика, проверьте механическое положение кулачка.\nЧто показал индикатор?";

        var result = BotResponseDeduplicator.RemoveRepeatedSteps(answer, previous);

        Assert.DoesNotContain("зафиксируйте кольцо", result);
        Assert.DoesNotContain("проверьте механическое", result);
        Assert.Contains("Что показал индикатор?", result);
    }

    [Fact]
    public void RewordedPriorActionAndItsSubstepsAreNotRepeated()
    {
        const string previous = "2. Проверьте и при необходимости отрегулируйте параметр «Натяжение на остановке»:\n"
            + "- Увеличьте значение, если рулон продолжает вращаться после остановки.";
        const string answer = "1. Отрегулируйте «Натяжение на остановке»:\n"
            + "- Если рулон продолжает вращаться, увеличьте значение.\n"
            + "Уточните, муфта вообще не срабатывает или не удерживает рулон?";

        var result = BotResponseDeduplicator.RemoveRepeatedSteps(answer, previous);

        Assert.DoesNotContain("Отрегулируйте", result);
        Assert.DoesNotContain("увеличьте значение", result);
        Assert.Contains("Уточните", result);
    }

    [Fact]
    public void RepeatedMeasurementIsKeptWhenClientRequestsTheValue()
    {
        const string previous = "2. Подготовьте эталонный груз массой не менее 16 кг.";
        const string answer = "Для калибровки подготовьте эталонный груз массой не менее 16 кг.";

        Assert.Contains("16 кг", BotResponseDeduplicator.RemoveRepeatedSteps(answer, previous));
    }

    [Fact]
    public void EmptyHistoryUsesOnlyCurrentQuestion()
    {
        var plan = BotConversationPlanner.Create("ALF показывает ошибку E34", null);

        Assert.False(plan.IsFollowUp);
        Assert.Equal("ALF показывает ошибку E34", plan.SearchQuestion);
    }

    [Theory]
    [InlineData("Как проверить этот датчик?", "Настройка датчика S03: индикатор гаснет при повороте.", true)]
    [InlineData("Выходной сигнал датчика", "Настройка датчика S03: индикатор гаснет при повороте.", true)]
    [InlineData("А как проверить датчик?", "Проверка датчика S04: индикатор должен мигать.", false)]
    [InlineData("А как настроить натяжение фольги?", "Настройка натяжения фольги выполняется по толщине.", false)]
    [InlineData("А как проверить клей?", "Настройка подачи клея на ALF.", true)]
    [InlineData("Какую массу груза использовать?", "Подготовьте эталонный груз массой не менее 16 кг.", false)]
    public void EvidenceGateChecksNewTermsAcrossDifferentAlfTopics(string question, string evidence, bool missing)
    {
        var chunks = new List<RagChunkCandidate> { new() { Text = evidence } };

        Assert.Equal(missing, BotEvidenceGate.MissingTerms(question, chunks).Count > 0);
    }

    [Theory]
    [InlineData("ru")]
    [InlineData("en")]
    public void TestChatUiHasBothLocales(string language)
    {
        foreach (var key in new[] { "BotTestMode", "BotChatMode", "BotSingleMode", "BotChatModeHint", "BotChatEmpty", "BotChatHistory", "ClearBotChat", "SupplementalRetrievalQuestion", "BotFollowUpEvidenceMissing" })
            Assert.NotEqual(key, UiText.ForLanguage(language, key));
    }
}

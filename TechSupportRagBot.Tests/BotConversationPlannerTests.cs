using TechSupportRagBot.Services;
using System.Text;

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

    [Theory]
    [InlineData("третий", "Серво3")]
    [InlineData("3-й", "Серво3")]
    [InlineData("второй", "Серво 2")]
    [InlineData("third", "Серво3")]
    public void OrdinalAnswerResolvesNumberedOptionFromPreviousBotQuestion(string reply, string expectedReference)
    {
        const string history = "Клиент: Ошибка сервопривода\n"
            + "Бот: Какой именно сервопривод показывает ошибку: Серво 2 приемный лоток или Серво3 подмотка 1? Какой код ошибки?";

        var plan = BotConversationPlanner.Create(reply, history);

        Assert.True(plan.IsFollowUp);
        Assert.Equal(expectedReference, plan.ResolvedReference);
        Assert.Contains(expectedReference, plan.SearchQuestion);
    }

    [Fact]
    public void OrdinalWithoutMatchingNamedOptionIsNotInvented()
    {
        const string history = "Клиент: Не запускается привод\nБот: Какой привод показывает ошибку?";

        var plan = BotConversationPlanner.Create("третий", history);

        Assert.True(plan.IsFollowUp);
        Assert.Null(plan.ResolvedReference);
    }

    [Fact]
    public void BareNumberDoesNotAssumeWhetherClientMeansServoOrErrorCode()
    {
        const string history = "Клиент: Ошибка сервопривода\nБот: Какой сервопривод: Серво3? Какой код ошибки?";

        Assert.Null(BotConversationPlanner.Create("3", history).ResolvedReference);
    }

    [Fact]
    public void OrdinalResolutionWorksForOtherNumberedComponents()
    {
        const string history = "Клиент: Не работает насос клея\nБот: Какой насос: Насос1 или Насос2?";

        Assert.Equal("Насос2", BotConversationPlanner.Create("второй", history).ResolvedReference);
    }

    [Fact]
    public void OrdinalResolutionSupportsLetterNumberComponentCodes()
    {
        const string history = "Клиент: Не срабатывает датчик\nБот: Какой датчик: S02 или S03?";

        Assert.Equal("S03", BotConversationPlanner.Create("третий", history).ResolvedReference);
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
    [InlineData("ocn", "Клиент: У меня ошибка на инверторе\nБот: Уточните код ошибки?", "ocn")]
    [InlineData("A003", "Клиент: Ошибка сервопривода\nБот: Какой код ошибки?", "A003")]
    [InlineData("S04", "Клиент: Не работает датчик\nБот: Какой датчик?", null)]
    [InlineData("муфта", "Клиент: Не работает тормозная\nБот: Какой узел?", null)]
    public void ShortAnswersAreClassifiedFromTheBotQuestion(string reply, string history, string? code)
    {
        var plan = BotConversationPlanner.Create(reply, history);
        Assert.True(plan.AnswersBotQuestion);
        Assert.Equal(code, BotTechnicalCode.ExtractForFollowUp(reply, history, plan.IsFollowUp));
    }

    [Fact]
    public void ShortLocationAnswerRetainsThePreviouslySuppliedFaultCode()
    {
        const string history = "Клиент: У меня ошибка на инверторе\nБот: Уточните код ошибки?\n"
            + "Клиент: ocn\nБот: На каком узле станка возникла ошибка?";
        var plan = BotConversationPlanner.Create("Узел размотки", history);

        Assert.True(plan.AnswersBotQuestion);
        Assert.Equal("ocn", BotTechnicalCode.ExtractForFollowUp("Узел размотки", history, plan.IsFollowUp));
        Assert.Contains("размотки", plan.SearchQuestion);
    }

    [Fact]
    public void MultilineAnswerIsPreservedAndWireQuestionStaysOnOriginalIssue()
    {
        const string history = "Клиент: Ошибка нити отрезки что делать?\n"
            + "Бот: Проверьте провода нити отрезки.\n"
            + "1. Замените повреждённые провода.\n"
            + "2. Проверьте блок питания нити.\n"
            + "Чем могу помочь ещё?";

        var turns = BotConversationHistory.Parse(history);
        Assert.Equal(2, turns.Count);
        Assert.Contains("Замените повреждённые провода", turns[1].Text);

        var plan = BotConversationPlanner.Create("Каких проводов?", history);
        Assert.True(plan.IsFollowUp);
        Assert.True(plan.AsksAboutPriorAnswer);
        Assert.Equal("Ошибка нити отрезки что делать?", plan.OriginalProblem);
        Assert.Contains("провода нити отрезки", plan.LastBotAnswer);
        Assert.Contains("Каких проводов?", plan.SearchQuestion);
    }

    [Fact]
    public void FollowUpFocusExcludesOtherWiringAndUnmentionedNeighborProcedure()
    {
        const string prior = "Проверьте подключение проводов нити отрезки. Замените повреждённые провода. "
            + "Убедитесь, что нить ничего не касается. Понаблюдайте за блоком питания нити: его индикатор должен гореть постоянно.";
        var discussed = new RagChunkCandidate
        {
            Title = "Предупреждение о нити отрезки",
            Solution = "Проверьте подключение проводов нити отрезки и замените повреждённые провода. "
                + "Проверить что нить ничего не касается. Понаблюдать за блоком питания нити, что индикатор горит постоянно."
        };
        var neighbor = new RagChunkCandidate
        {
            Title = "После замены нити отрезки нет нагрева",
            Solution = "Проверьте гибкие силовые кабели к подвижному узлу нити отрезки."
        };
        var unrelated = new RagChunkCandidate
        {
            Title = "Подключение датчика E3RB-DN22",
            Solution = "Соедините белый и коричневый провода с красным проводом."
        };

        var selected = BotFollowUpFocus.Select([unrelated, neighbor, discussed],
            "Ошибка нити отрезки что делать?", prior);

        Assert.Single(selected);
        Assert.Same(discussed, selected[0]);
        Assert.Empty(BotFollowUpFocus.Select([unrelated], "Ошибка нити отрезки что делать?", prior));
    }

    [Fact]
    public void UnrelatedNewQuestionDoesNotInheritPreviousAnswer()
    {
        const string history = "Клиент: Ошибка нити отрезки\nБот: Проверьте провода нити отрезки.";
        Assert.False(BotConversationPlanner.Create("Как настроить датчик?", history).IsFollowUp);
    }

    [Fact]
    public void FaultCodeMatchesWholeIdentifierInEvidence()
    {
        var exact = new RagChunkCandidate { ErrorCode = "ocn", Text = "Авария ocn: перегрузка по току." };
        var neighbor = new RagChunkCandidate { ErrorCode = "oc2", Text = "Авария oc2." };
        Assert.True(BotTechnicalCode.Matches(exact, "OCN"));
        Assert.False(BotTechnicalCode.Matches(neighbor, "ocn"));
    }

    [Fact]
    public void NormalRussianHistoryIsNotMistakenForMojibake()
    {
        const string history = "Клиент: ALF не подаёт этикетку\nБот: Рулон остановился. Проверьте датчик S04.";

        Assert.Equal(history, TextEncodingRepairService.RepairIfNeeded(history));
    }

    [Fact]
    public void ActuallyBrokenRussianEncodingCanStillBeRepaired()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        const string original = "Привет, проверьте рулон";
        var broken = Encoding.GetEncoding(1251).GetString(Encoding.UTF8.GetBytes(original));

        Assert.Equal(original, TextEncodingRepairService.RepairIfNeeded(broken));
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

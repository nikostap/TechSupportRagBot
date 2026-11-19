using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TechSupportRagBot.Data;
using TechSupportRagBot.Models;
using TechSupportRagBot.Services;

namespace TechSupportRagBot.Tests;

public sealed class BotTestTraceTests
{
    [Fact]
    public void LiveProgressIsVisibleOnlyToItsOwner()
    {
        var store = new BotTestProgressStore();
        var requestId = Guid.NewGuid();
        store.Start(requestId, "admin-1");
        store.Add(requestId, new BotDecisionStep("BotTraceHybridSearch", "ошибка нет шпули"));

        Assert.Single(store.Read(requestId, "admin-1"));
        Assert.Empty(store.Read(requestId, "admin-2"));
    }

    [Fact]
    public async Task SavedTestSessionSurvivesASeparateDbContext()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using (var db = new ApplicationDbContext(options))
        {
            await db.Database.EnsureCreatedAsync();
            db.BotTestSessions.Add(new BotTestSession
            {
                UserId = "admin-1", MachineId = 800, Language = "Russian",
                HistoryJson = "protected-transcript"
            });
            await db.SaveChangesAsync();
        }

        await using (var db = new ApplicationDbContext(options))
        {
            var saved = await db.BotTestSessions.SingleAsync(x => x.UserId == "admin-1");
            Assert.Equal(800, saved.MachineId);
            Assert.Equal("protected-transcript", saved.HistoryJson);
        }
    }

    [Theory]
    [InlineData("ru")]
    [InlineData("en")]
    public void EveryDisplayedTraceStepHasATranslation(string language)
    {
        var keys = new[]
        {
            "BotTraceTitle", "BotTraceNewQuestion", "BotTraceClarification",
            "BotTraceAffirmation", "BotTraceNegation", "BotTraceCourtesy", "BotTraceUncertainIntent",
            "BotTraceIntentFromModel", "BotTraceIntentFromRules", "BotTraceFollowUpPlan",
            "BotTraceNewSearchPlan", "BotTraceOperatorRequested", "BotTraceCodeChoiceNeeded",
            "BotTraceCodeRejected", "BotTraceChoiceNeeded", "BotTraceDetailNeeded",
            "BotTraceHybridSearch", "BotTraceExactCodeSearch", "BotTraceAdditionalSearch",
            "BotTraceNoSources", "BotTraceSources", "BotTraceSourceChat",
            "BotTraceSourceDocument", "BotTraceNearCode", "BotTraceCodeNotFound",
            "BotTraceClarificationFromSearch", "BotTraceEvidenceMissing", "BotTraceDirectManual",
            "BotTraceDirectQa", "BotTraceModelAnswer", "BotTraceModelFallbackQa",
            "BotTraceModelUnavailable", "BotTraceSourceOnlyAnswer", "BotTraceOperatorHandoff",
            "BotTraceAnswerReady", "BotTraceRepeatedStepsRemoved", "BotTraceMediaAttached",
            "BotTraceNoMedia"
        };
        foreach (var key in keys)
            Assert.NotEqual(key, UiText.ForLanguage(language, key));
        Assert.NotEqual("BotCourtesyReply", UiText.ForLanguage(language, "BotCourtesyReply"));
        foreach (var key in new[] { "BotAcknowledgementReply", "BotGreetingReply", "BotFarewellReply" })
            Assert.NotEqual(key, UiText.ForLanguage(language, key));
    }
}

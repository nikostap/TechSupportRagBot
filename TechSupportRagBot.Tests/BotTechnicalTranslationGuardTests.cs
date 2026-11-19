using TechSupportRagBot.Services;

namespace TechSupportRagBot.Tests;

public class BotTechnicalTranslationGuardTests
{
    [Fact]
    public void RequiresAllTechnicalNumbersAndEnglishText()
    {
        const string source = "Давление 6.5 атм, реле 2.5 атм, затем 3 атм.";
        Assert.True(BotTechnicalTranslationGuard.IsReliableEnglish(source,
            "Pressure is 6.5 atm, switch at 2.5 atm, then 3 atm."));
        Assert.False(BotTechnicalTranslationGuard.IsReliableEnglish(source,
            "Pressure is 6.5 atm, switch at 2.5 atm."));
        Assert.False(BotTechnicalTranslationGuard.IsReliableEnglish(source,
            "Pressure is 6.5 atm, реле at 2.5 atm, then 3 atm."));
    }
}

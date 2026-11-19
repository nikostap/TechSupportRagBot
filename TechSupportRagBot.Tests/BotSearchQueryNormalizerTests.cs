using TechSupportRagBot.Services;

namespace TechSupportRagBot.Tests;

public class BotSearchQueryNormalizerTests
{
    [Fact]
    public void NormalizesTranslatedShopFloorVocabulary()
    {
        Assert.Equal("станок пишет, что нет шпули",
            BotSearchQueryNormalizer.NormalizeTranslatedRussian("Машина говорит, что нет катушки"));
    }
}

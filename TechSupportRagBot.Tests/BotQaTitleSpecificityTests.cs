using TechSupportRagBot.Services;

namespace TechSupportRagBot.Tests;

public class BotQaTitleSpecificityTests
{
    [Fact]
    public void PenalizesUnrequestedPairOfSensorIds()
    {
        var qa = new RagChunkCandidate { DocumentType = "QA",
            Title = "При подаче шпули ошибки датчиков 85 и 81" };
        Assert.True(BotQaTitleSpecificity.UnrequestedSensorPenalty("Станок не видит шпулю", qa) < 0);
        Assert.Equal(0, BotQaTitleSpecificity.UnrequestedSensorPenalty("Ошибка датчиков 85 и 81", qa));
    }
}

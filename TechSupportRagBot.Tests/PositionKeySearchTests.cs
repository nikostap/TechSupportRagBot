using TechSupportRagBot.Models;
using TechSupportRagBot.Services;

namespace TechSupportRagBot.Tests;

public sealed class PositionKeySearchTests
{
    [Fact]
    public void MatchesOnlyCompleteDesignation()
    {
        Assert.True(PositionKeySearch.MatchesKey("Как подключить датчик 61?", "61"));
        Assert.False(PositionKeySearch.MatchesKey("Как подключить датчик 161?", "61"));
        Assert.False(PositionKeySearch.MatchesKey("Ошибка 61", "61"));
        Assert.True(PositionKeySearch.MatchesKey("Датчик S50 не работает", "S50"));
    }

    [Fact]
    public void SharedDesignationFindsBothInstalledParts()
    {
        var installed = new[]
        {
            Item("75.fiber"), Item("75.amplifier"), Item("61")
        };
        var matches = PositionKeySearch.Match("Что установлено в позиции 75?", installed);
        Assert.Equal(2, matches.Count);
        Assert.DoesNotContain(matches, item => item.Position.Key == "61");
    }

    private static ResolvedComponent Item(string key) => new(
        new MachinePosition { Key = key, MachineModel = "L", Name = key },
        new ComponentModel { PartNumber = key });
}

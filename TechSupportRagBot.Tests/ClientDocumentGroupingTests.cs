using TechSupportRagBot.Models;
using TechSupportRagBot.Pages.Client;

namespace TechSupportRagBot.Tests;

public sealed class ClientDocumentGroupingTests
{
    [Theory]
    [InlineData("MachineManual", "machine")]
    [InlineData("ElectricalScheme", "general")]
    [InlineData("PneumaticScheme", "general")]
    [InlineData("SetupGuide", "general")]
    public void MachineAndGeneralDocumentsHaveTheirOwnCategories(string kind, string expected)
    {
        Assert.Equal(expected, MachineDocumentsModel.GroupKey(new KnowledgeDocument { DocumentKind = kind }));
    }

    [Fact]
    public void DeviceDocumentsAreGroupedByComponentType()
    {
        var servo = new ComponentModel { Type = "Сервопривод" };
        var sensor = new ComponentModel { Type = "Датчик" };
        Assert.Equal("device:Сервопривод", MachineDocumentsModel.GroupKey(new KnowledgeDocument
        {
            DocumentKind = "ComponentManual", ComponentModel = servo
        }));
        Assert.Equal("device:Датчик", MachineDocumentsModel.GroupKey(new KnowledgeDocument
        {
            DocumentKind = "SetupGuide", ComponentModel = sensor
        }));
        Assert.Equal("general", MachineDocumentsModel.GroupKey(new KnowledgeDocument
        {
            DocumentKind = "ElectricalScheme", ComponentModel = servo
        }));
    }
}

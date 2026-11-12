using System.Text;
using TechSupportRagBot.Models;
using TechSupportRagBot.Services;

namespace TechSupportRagBot.Tests;

public sealed class PreparedManualServiceTests
{
    [Fact]
    public async Task ValidManualKeepsTopicBoundariesAndSourceReferences()
    {
        var json = """
            {"schemaVersion":1,"title":"Drive manual","sections":[
              {"id":"alarm-a","title":"Alarm A","text":"Check the wiring and compare the alarm with the controller manual before resetting the drive.","keywords":["alarm A"],"tags":["diagnostics"],"questions":["What does alarm A mean?"],"node":"Drive","errorCode":"A","sourcePage":12},
              {"id":"parameter-1","title":"Parameter 1","text":"Set this parameter only after verifying the motor rating and the allowed range in the manual.","keywords":["parameter 1"],"questions":["How is parameter 1 set?"],"sourcePage":30}
            ]}
            """;
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var manual = await new PreparedManualService().ReadAsync(stream);
        var chunks = PreparedManualService.ToChunks(manual, new KnowledgeDocument { OriginalFileName = "source.pdf" });

        Assert.Equal(2, chunks.Count);
        Assert.Equal("A", chunks[0].ErrorCode);
        Assert.Equal(12, chunks[0].Page);
        Assert.Equal("source.pdf", chunks[0].FileName);
        Assert.DoesNotContain("Parameter 1", chunks[0].Text);
    }

    [Theory]
    [InlineData("{\"schemaVersion\":2,\"title\":\"x\",\"sections\":[]}")]
    [InlineData("{\"schemaVersion\":1,\"title\":\"x\",\"sections\":[{\"id\":\"x\",\"title\":\"X\",\"text\":\"Missing searchable metadata but a sufficiently long explanatory text is supplied.\"}]}")]
    [InlineData("not JSON")]
    public async Task RejectsInvalidOrIncompleteManual(string json)
    {
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        await Assert.ThrowsAsync<InvalidDataException>(() => new PreparedManualService().ReadAsync(stream));
    }
}

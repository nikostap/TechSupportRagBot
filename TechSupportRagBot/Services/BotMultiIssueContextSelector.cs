namespace TechSupportRagBot.Services;

/// <summary>Limits an explicitly combined fault report to the verified QA matched for each fault.</summary>
public static class BotMultiIssueContextSelector
{
    public static IReadOnlyList<RagChunkCandidate> Select(string question,
        IReadOnlyList<RagChunkCandidate> chunks)
    {
        var ids = BotAnswerMediaSelector.SelectQaEntryIds(question, chunks);
        if (ids.Count < 2) return chunks;
        var selected = chunks.Where(x => x.QAEntryId.HasValue && ids.Contains(x.QAEntryId.Value)).ToList();
        return selected.Select(x => x.QAEntryId).Distinct().Count() == ids.Count ? selected : chunks;
    }
}

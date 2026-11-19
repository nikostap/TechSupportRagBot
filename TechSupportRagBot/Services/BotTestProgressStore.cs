using System.Collections.Concurrent;

namespace TechSupportRagBot.Services;

public sealed class BotTestProgressStore
{
    private sealed class Progress(string userId)
    {
        public string UserId { get; } = userId;
        public DateTime CreatedAt { get; } = DateTime.UtcNow;
        public List<BotDecisionStep> Steps { get; } = [];
    }

    private readonly ConcurrentDictionary<Guid, Progress> _requests = new();

    public void Start(Guid requestId, string userId)
    {
        foreach (var item in _requests.Where(x => x.Value.CreatedAt < DateTime.UtcNow.AddMinutes(-10)))
            _requests.TryRemove(item.Key, out _);
        _requests[requestId] = new Progress(userId);
    }

    public void Add(Guid requestId, BotDecisionStep step)
    {
        if (!_requests.TryGetValue(requestId, out var progress)) return;
        lock (progress.Steps)
        {
            if (progress.Steps.Count < 30) progress.Steps.Add(step);
        }
    }

    public IReadOnlyList<BotDecisionStep> Read(Guid requestId, string userId)
    {
        if (!_requests.TryGetValue(requestId, out var progress) || progress.UserId != userId) return [];
        lock (progress.Steps) return progress.Steps.ToList();
    }
}

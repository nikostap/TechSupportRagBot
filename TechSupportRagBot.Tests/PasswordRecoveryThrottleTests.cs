using Microsoft.Extensions.Caching.Memory;
using TechSupportRagBot.Services;

namespace TechSupportRagBot.Tests;

public sealed class PasswordRecoveryThrottleTests
{
    [Fact]
    public void First_request_is_allowed_and_immediate_repeat_is_rejected()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var throttle = new PasswordRecoveryThrottle(cache);

        Assert.True(throttle.AllowRequest("person@example.com", "192.0.2.1"));
        Assert.False(throttle.AllowRequest("PERSON@example.com", "192.0.2.1"));
        Assert.True(throttle.AllowRequest("other@example.com", "192.0.2.1"));
    }
}

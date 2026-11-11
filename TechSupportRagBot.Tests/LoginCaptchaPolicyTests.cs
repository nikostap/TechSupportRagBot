using System.Net;
using Microsoft.AspNetCore.Http;
using TechSupportRagBot.Services;

namespace TechSupportRagBot.Tests;

public sealed class LoginCaptchaPolicyTests
{
    [Theory]
    [InlineData("localhost", "127.0.0.1", false)]
    [InlineData("127.0.0.1", "::1", false)]
    [InlineData("[::1]", "127.0.0.1", false)]
    [InlineData("support.classeng.com", "127.0.0.1", true)]
    [InlineData("localhost", "203.0.113.5", true)]
    [InlineData("support.classeng.com", "203.0.113.5", true)]
    public void RequiresCaptchaExceptForDirectLoopbackRequests(string host, string remoteIp, bool expected)
    {
        var context = new DefaultHttpContext();
        context.Request.Host = new HostString(host);
        context.Connection.RemoteIpAddress = IPAddress.Parse(remoteIp);

        Assert.Equal(expected, LoginCaptchaPolicy.IsRequired(context));
    }
}

using System.Net;

namespace TechSupportRagBot.Services;

public static class LoginCaptchaPolicy
{
    public static bool IsRequired(HttpContext context)
    {
        var host = context.Request.Host.Host.Trim('[', ']');
        var localHost = string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
            || string.Equals(host, "127.0.0.1", StringComparison.Ordinal)
            || string.Equals(host, "::1", StringComparison.Ordinal);
        var remoteAddress = context.Connection.RemoteIpAddress;
        var localConnection = remoteAddress != null && IPAddress.IsLoopback(
            remoteAddress.IsIPv4MappedToIPv6 ? remoteAddress.MapToIPv4() : remoteAddress);
        return !(localHost && localConnection);
    }
}

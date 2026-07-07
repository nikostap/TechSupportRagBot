using System.Security.Cryptography;

namespace TechSupportRagBot.Services;

public static class LicenseKeyGenerator
{
    public static string Generate()
    {
        Span<byte> bytes = stackalloc byte[15];
        RandomNumberGenerator.Fill(bytes);

        var token = Convert.ToHexString(bytes);

        return string.Join('-', Enumerable.Range(0, 5).Select(i => token.Substring(i * 6, 6)));
    }
}

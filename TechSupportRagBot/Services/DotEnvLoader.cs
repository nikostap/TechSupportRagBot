using Npgsql;

namespace TechSupportRagBot.Services;

public static class DotEnvLoader
{
    public static void LoadForLocalDevelopment()
    {
        var current = Directory.GetCurrentDirectory();
        var candidates = new[]
        {
            Path.Combine(current, ".env"),
            Path.Combine(Directory.GetParent(current)?.FullName ?? current, ".env")
        };
        var path = candidates.FirstOrDefault(File.Exists);
        if (path == null)
        {
            return;
        }

        foreach (var rawLine in File.ReadLines(path))
        {
            var line = rawLine.Trim().TrimStart('\uFEFF');
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith("export ", StringComparison.OrdinalIgnoreCase))
            {
                line = line["export ".Length..].TrimStart();
            }

            var separator = line.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var name = line[..separator].Trim();
            if (name.Length == 0 || Environment.GetEnvironmentVariable(name) != null)
            {
                continue;
            }

            var value = line[(separator + 1)..].Trim();
            if (value.Length >= 2
                && ((value[0] == '"' && value[^1] == '"')
                    || (value[0] == '\'' && value[^1] == '\'')))
            {
                value = value[1..^1];
            }

            Environment.SetEnvironmentVariable(name, value);
        }

        NormalizeDockerAddressesForLocalProcess();
    }

    private static void NormalizeDockerAddressesForLocalProcess()
    {
        if (string.Equals(
            Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"),
            "true",
            StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        const string connectionVariable = "ConnectionStrings__DefaultConnection";
        var connectionString = Environment.GetEnvironmentVariable(connectionVariable);
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            try
            {
                var builder = new NpgsqlConnectionStringBuilder(connectionString);
                if (string.Equals(builder.Host, "postgres", StringComparison.OrdinalIgnoreCase))
                {
                    builder.Host = "localhost";
                    if (builder.Port == 5432)
                    {
                        builder.Port = 55432;
                    }

                    Environment.SetEnvironmentVariable(connectionVariable, builder.ConnectionString);
                }
            }
            catch (ArgumentException)
            {
                // ASP.NET Core will report a malformed connection string in the normal startup path.
            }
        }

        NormalizeServiceUrl("Rag__QdrantBaseUrl", "qdrant");
        NormalizeServiceUrl("Rag__OllamaBaseUrl", "ollama");
        NormalizeServiceUrl("LibreTranslate__BaseUrl", "libretranslate");
    }

    private static void NormalizeServiceUrl(string variable, string dockerHost)
    {
        var value = Environment.GetEnvironmentVariable(variable);
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Host, dockerHost, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var local = new UriBuilder(uri) { Host = "localhost" };
        Environment.SetEnvironmentVariable(variable, local.Uri.ToString().TrimEnd('/'));
    }
}

namespace TechSupportRagBot.Services;

public class RagOptions
{
    public string ChatProvider { get; set; } = "Ollama";

    public string EmbeddingProvider { get; set; } = "Ollama";

    public string OllamaBaseUrl { get; set; } = "http://localhost:11434";

    public string QdrantBaseUrl { get; set; } = "http://localhost:6333";

    public string ChatModel { get; set; } = "llama3.1";

    public string EmbeddingModel { get; set; } = "embeddinggemma";

    public string CollectionName { get; set; } = "techsupport_knowledge";
}

public class OpenAiOptions
{
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";

    public string ApiKey { get; set; } = string.Empty;

    public string ChatModel { get; set; } = "gpt-4.1-mini";

    public string EmbeddingModel { get; set; } = "text-embedding-3-small";
}

public class DeepSeekOptions
{
    public string BaseUrl { get; set; } = "https://api.deepseek.com";

    public string ApiKey { get; set; } = string.Empty;

    public string ChatModel { get; set; } = "deepseek-chat";
}

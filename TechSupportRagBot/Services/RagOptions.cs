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

public class QwenOptions
{
    public string BaseUrl { get; set; } = "https://ws-am1n1jqyhug10mfy.ap-southeast-1.maas.aliyuncs.com/compatible-mode/v1";

    public string ApiKey { get; set; } = string.Empty;

    public string EmbeddingModel { get; set; } = "text-embedding-v4";

    public int EmbeddingDimensions { get; set; } = 1024;
}

public class AiTunnelOptions
{
    public string BaseUrl { get; set; } = "https://api.aitunnel.ru/v1";

    public string ApiKey { get; set; } = string.Empty;

    public string ChatModel { get; set; } = "auto";

    public string EmbeddingModel { get; set; } = "text-embedding-v4";
}

public class LibreTranslateOptions
{
    public string BaseUrl { get; set; } = "http://localhost:5000";

    public string ApiKey { get; set; } = string.Empty;
}

public class FastTextLanguageOptions
{
    public string ModelPath { get; set; } = "Resources/fasttext/lid.176.ftz";

    public float MinConfidence { get; set; } = 0.35f;
}

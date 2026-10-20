namespace TechSupportRagBot.Services;

public static class SystemSettingKeys
{
    public const string RagChatProvider = "Rag.ChatProvider";
    public const string RagEmbeddingProvider = "Rag.EmbeddingProvider";
    public const string OllamaChatModel = "Ollama.ChatModel";
    public const string OllamaEmbeddingModel = "Ollama.EmbeddingModel";
    public const string UnreadEmailDelayMinutes = "Notifications.UnreadEmailDelayMinutes";
    public const string AccessProfiles = "Access.Profiles";
    public const string StorageProvider = "Storage.Provider";
    public const string MachineModels = "Machines.Models";
    public const string EmailNotificationRules = "Notifications.EmailRules";
}

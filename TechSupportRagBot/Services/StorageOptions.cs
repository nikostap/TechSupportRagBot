namespace TechSupportRagBot.Services;

public sealed class StorageOptions
{
    public string Provider { get; set; } = "Local";

    public string LocalRoot { get; set; } = "App_Data/Uploads";

    public S3StorageOptions S3 { get; set; } = new();
}

public sealed class S3StorageOptions
{
    public string ServiceUrl { get; set; } = "https://s3.yandexcloud.net";

    public string Region { get; set; } = "ru-central1";

    public string Bucket { get; set; } = string.Empty;

    public string AccessKey { get; set; } = string.Empty;

    public string SecretKey { get; set; } = string.Empty;
}

namespace TechSupportRagBot.Services;

public class VideoProcessingOptions
{
    public string FfmpegPath { get; set; } = "ffmpeg";

    public string FfprobePath { get; set; } = "ffprobe";

    public int MaxUploadSizeMb { get; set; } = 200;

    public int Crf { get; set; } = 28;

    public string Preset { get; set; } = "veryfast";

    public string AudioBitrate { get; set; } = "96k";

    public int MaxWidth { get; set; } = 1280;

    public bool DeleteOriginalAfterSuccess { get; set; } = true;
}

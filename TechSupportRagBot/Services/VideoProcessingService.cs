using System.Diagnostics;
using Microsoft.Extensions.Options;
using TechSupportRagBot.Models;

namespace TechSupportRagBot.Services;

public class VideoProcessingService : IVideoProcessingService
{
    private readonly IWebHostEnvironment _environment;
    private readonly VideoProcessingOptions _options;

    public VideoProcessingService(IWebHostEnvironment environment, IOptions<VideoProcessingOptions> options)
    {
        _environment = environment;
        _options = options.Value;
    }

    public async Task<VideoProcessingResult> ProcessAsync(
        Attachment attachment,
        Func<int, string, Task>? progress,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(attachment.TempFilePath))
        {
            throw new InvalidOperationException("Не указан временный файл видео.");
        }

        // Сначала создаем итоговое видео и превью, и только после успеха удаляем исходник.
        var inputPath = ToAbsolutePath(attachment.TempFilePath);
        if (progress != null)
        {
            await progress(20, "Видео преобразовывается в MP4");
        }

        var finalPath = await ConvertToMp4Async(inputPath, attachment.PublicId, cancellationToken);
        if (progress != null)
        {
            await progress(80, "Создаётся превью");
        }

        var previewPath = await CreatePreviewAsync(inputPath, attachment.PublicId, cancellationToken);

        if (_options.DeleteOriginalAfterSuccess && File.Exists(inputPath))
        {
            File.Delete(inputPath);
        }

        var finalRelative = ToRelativePath(finalPath);
        var previewRelative = ToRelativePath(previewPath);
        var size = new FileInfo(finalPath).Length;
        return new VideoProcessingResult(finalRelative, previewRelative, size);
    }

    public async Task<string> ConvertToMp4Async(string inputPath, Guid attachmentId, CancellationToken cancellationToken)
    {
        var outputDir = EnsureWebFolder("uploads", "videos");
        var outputPath = Path.Combine(outputDir, $"{attachmentId:N}.mp4");
        var scale = $"scale='min({_options.MaxWidth},iw)':-2";
        var args = $"-y -i {Quote(inputPath)} -c:v libx264 -preset {_options.Preset} -crf {_options.Crf} -vf {Quote(scale)} -c:a aac -b:a {_options.AudioBitrate} -movflags +faststart {Quote(outputPath)}";
        await RunProcessAsync(_options.FfmpegPath, args, cancellationToken);
        return outputPath;
    }

    public async Task<string> CreatePreviewAsync(string inputPath, Guid attachmentId, CancellationToken cancellationToken)
    {
        var outputDir = EnsureWebFolder("uploads", "previews");
        var outputPath = Path.Combine(outputDir, $"{attachmentId:N}.jpg");
        var args = $"-y -ss 00:00:01 -i {Quote(inputPath)} -frames:v 1 {Quote(outputPath)}";
        try
        {
            await RunProcessAsync(_options.FfmpegPath, args, cancellationToken);
        }
        catch
        {
            // У очень коротких роликов кадра на первой секунде может не быть.
            var fallbackArgs = $"-y -i {Quote(inputPath)} -frames:v 1 {Quote(outputPath)}";
            await RunProcessAsync(_options.FfmpegPath, fallbackArgs, cancellationToken);
        }

        return outputPath;
    }

    public async Task<VideoMetadata> GetMetadataAsync(string inputPath, CancellationToken cancellationToken)
    {
        try
        {
            var args = $"-v error -show_entries stream=width,height -show_entries format=duration -of default=noprint_wrappers=1 {Quote(inputPath)}";
            var output = await RunProcessAsync(_options.FfprobePath, args, cancellationToken);
            int? width = TryReadInt(output, "width=");
            int? height = TryReadInt(output, "height=");
            TimeSpan? duration = TryReadDouble(output, "duration=", out var seconds)
                ? TimeSpan.FromSeconds(seconds)
                : null;
            return new VideoMetadata(duration, width, height);
        }
        catch
        {
            return new VideoMetadata(null, null, null);
        }
    }

    private async Task<string> RunProcessAsync(string fileName, string arguments, CancellationToken cancellationToken)
    {
        // FFmpeg может долго писать stderr, поэтому читаем оба потока до проверки ExitCode.
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };

        process.Start();
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var output = await outputTask;
        var error = await errorTask;

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"FFmpeg завершился с кодом {process.ExitCode}: {error}");
        }

        return string.IsNullOrWhiteSpace(output) ? error : output;
    }

    private string EnsureWebFolder(params string[] parts)
    {
        var root = _environment.WebRootPath ?? Path.Combine(_environment.ContentRootPath, "wwwroot");
        var path = Path.Combine(new[] { root }.Concat(parts).ToArray());
        Directory.CreateDirectory(path);
        return path;
    }

    private string ToAbsolutePath(string relativePath)
    {
        var root = _environment.WebRootPath ?? Path.Combine(_environment.ContentRootPath, "wwwroot");
        return Path.Combine(root, relativePath);
    }

    private string ToRelativePath(string absolutePath)
    {
        var root = _environment.WebRootPath ?? Path.Combine(_environment.ContentRootPath, "wwwroot");
        return Path.GetRelativePath(root, absolutePath).Replace("\\", "/");
    }

    private static string Quote(string value) => $"\"{value}\"";

    private static int? TryReadInt(string text, string key)
    {
        var line = text.Split('\n').FirstOrDefault(x => x.StartsWith(key, StringComparison.OrdinalIgnoreCase));
        return int.TryParse(line?[key.Length..].Trim(), out var value) ? value : null;
    }

    private static bool TryReadDouble(string text, string key, out double value)
    {
        var line = text.Split('\n').FirstOrDefault(x => x.StartsWith(key, StringComparison.OrdinalIgnoreCase));
        return double.TryParse(line?[key.Length..].Trim(), System.Globalization.CultureInfo.InvariantCulture, out value);
    }
}


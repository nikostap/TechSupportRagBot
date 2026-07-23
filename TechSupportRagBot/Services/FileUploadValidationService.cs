using System.IO.Compression;
using System.Text;
using SkiaSharp;

namespace TechSupportRagBot.Services;

public sealed class FileUploadValidationService
{
    private const long ImageMaxBytes = 20L * 1024 * 1024;
    private const long AvatarMaxBytes = 10L * 1024 * 1024;
    private const long DocumentMaxBytes = 50L * 1024 * 1024;
    private const long VideoMaxBytes = 500L * 1024 * 1024;
    private const long MaxImagePixels = 50_000_000;

    private static readonly Dictionary<string, string[]> AllowedMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = ["image/jpeg"],
        [".jpeg"] = ["image/jpeg"],
        [".png"] = ["image/png"],
        [".webp"] = ["image/webp"],
        [".heic"] = ["image/heic", "image/heif", "application/octet-stream"],
        [".pdf"] = ["application/pdf"],
        [".docx"] = ["application/vnd.openxmlformats-officedocument.wordprocessingml.document", "application/octet-stream"],
        [".xlsx"] = ["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "application/octet-stream"],
        [".txt"] = ["text/plain", "application/octet-stream"],
        [".mp4"] = ["video/mp4", "application/mp4", "application/octet-stream"],
        [".mov"] = ["video/quicktime", "application/octet-stream"],
        [".avi"] = ["video/x-msvideo", "video/avi", "application/octet-stream"]
    };

    public async Task<ValidatedUpload> ValidateAsync(
        IFormFile file,
        UploadPurpose purpose,
        CancellationToken cancellationToken = default)
    {
        if (file.Length <= 0)
        {
            throw new InvalidOperationException("Файл пуст.");
        }

        var originalName = SafeFileName(file.FileName);
        var extension = Path.GetExtension(originalName).ToLowerInvariant();
        if (!AllowedMimeTypes.TryGetValue(extension, out var allowedMimes))
        {
            throw new InvalidOperationException("Тип файла запрещён. Поддерживаются JPEG, PNG, WEBP, HEIC, PDF, DOCX, XLSX, TXT, MP4, MOV и AVI.");
        }

        var kind = KindFor(extension);
        if (purpose == UploadPurpose.Avatar && kind != UploadKind.Image)
        {
            throw new InvalidOperationException("Аватар должен быть изображением JPEG, PNG, WEBP или HEIC.");
        }

        var maxBytes = purpose == UploadPurpose.Avatar
            ? AvatarMaxBytes
            : kind switch
            {
                UploadKind.Image => ImageMaxBytes,
                UploadKind.Document => DocumentMaxBytes,
                UploadKind.Video => VideoMaxBytes,
                _ => 0
            };
        if (file.Length > maxBytes)
        {
            throw new InvalidOperationException($"Размер файла превышает лимит {maxBytes / 1024 / 1024} МБ.");
        }

        var suppliedMime = (file.ContentType ?? string.Empty).Trim();
        if (!allowedMimes.Contains(suppliedMime, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"MIME-тип {suppliedMime} не соответствует расширению {extension}.");
        }

        var stagingRoot = Path.Combine(Path.GetTempPath(), "TechSupportRagBot", "UploadStaging");
        Directory.CreateDirectory(stagingRoot);
        var stagingPath = Path.Combine(stagingRoot, $"{Guid.NewGuid():N}.upload");
        Stream content = new FileStream(
            stagingPath,
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.None,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.DeleteOnClose);
        try
        {
            await using (var source = file.OpenReadStream())
            {
                var buffer = new byte[81920];
                long total = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    total += read;
                    if (total > maxBytes)
                    {
                        throw new InvalidOperationException($"Р Р°Р·РјРµСЂ С„Р°Р№Р»Р° РїСЂРµРІС‹С€Р°РµС‚ Р»РёРјРёС‚ {maxBytes / 1024 / 1024} РњР‘.");
                    }

                    await content.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
            }

            content.Position = 0;
            await ValidateSignatureAsync(content, extension, cancellationToken);

            if (kind == UploadKind.Image && extension != ".heic")
            {
                var normalized = await NormalizeImageAsync(content, extension, cancellationToken);
                await content.DisposeAsync();
                content = normalized;
            }

            content.Position = 0;
            return new ValidatedUpload(
                content,
                originalName,
                extension,
                CanonicalContentType(extension),
                kind,
                content.Length);
        }
        catch
        {
            await content.DisposeAsync();
            throw;
        }
    }

    private static async Task ValidateSignatureAsync(Stream stream, string extension, CancellationToken cancellationToken)
    {
        var header = new byte[Math.Min(64, (int)stream.Length)];
        await stream.ReadExactlyAsync(header, cancellationToken);
        stream.Position = 0;

        var valid = extension switch
        {
            ".jpg" or ".jpeg" => StartsWith(header, [0xFF, 0xD8, 0xFF]),
            ".png" => StartsWith(header, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]),
            ".webp" => Ascii(header, 0, 4) == "RIFF" && Ascii(header, 8, 4) == "WEBP",
            ".heic" => IsHeic(header),
            ".pdf" => Ascii(header, 0, 5) == "%PDF-",
            ".docx" => IsOfficePackage(stream, "word/"),
            ".xlsx" => IsOfficePackage(stream, "xl/"),
            ".txt" => IsUtf8Text(stream),
            ".mp4" => IsIsoMedia(header, false),
            ".mov" => IsIsoMedia(header, true),
            ".avi" => Ascii(header, 0, 4) == "RIFF" && Ascii(header, 8, 4) == "AVI ",
            _ => false
        };

        stream.Position = 0;
        if (!valid)
        {
            throw new InvalidOperationException($"Содержимое файла не соответствует формату {extension}.");
        }
    }

    private static async Task<MemoryStream> NormalizeImageAsync(Stream input, string extension, CancellationToken cancellationToken)
    {
        input.Position = 0;
        using var codec = SKCodec.Create(input)
            ?? throw new InvalidOperationException("Изображение повреждено или не поддерживается.");
        if ((long)codec.Info.Width * codec.Info.Height > MaxImagePixels)
        {
            throw new InvalidOperationException("Разрешение изображения слишком велико.");
        }

        using var bitmap = new SKBitmap(codec.Info);
        var result = codec.GetPixels(codec.Info, bitmap.GetPixels());
        if (result is not SKCodecResult.Success and not SKCodecResult.IncompleteInput)
        {
            throw new InvalidOperationException("Изображение не удалось безопасно декодировать.");
        }

        using var image = SKImage.FromBitmap(bitmap);
        var format = extension switch
        {
            ".jpg" or ".jpeg" => SKEncodedImageFormat.Jpeg,
            ".png" => SKEncodedImageFormat.Png,
            ".webp" => SKEncodedImageFormat.Webp,
            _ => throw new InvalidOperationException("Неподдерживаемый формат изображения.")
        };
        var quality = format == SKEncodedImageFormat.Png ? 100 : 90;
        using var encoded = image.Encode(format, quality)
            ?? throw new InvalidOperationException("Изображение не удалось безопасно перекодировать.");
        var output = new MemoryStream();
        encoded.SaveTo(output);
        await output.FlushAsync(cancellationToken);
        output.Position = 0;
        return output;
    }

    private static bool IsOfficePackage(Stream stream, string requiredPrefix)
    {
        try
        {
            stream.Position = 0;
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            return archive.GetEntry("[Content_Types].xml") != null
                && archive.Entries.Any(x => x.FullName.StartsWith(requiredPrefix, StringComparison.OrdinalIgnoreCase));
        }
        catch (InvalidDataException)
        {
            return false;
        }
        finally
        {
            stream.Position = 0;
        }
    }

    private static bool IsUtf8Text(Stream stream)
    {
        try
        {
            stream.Position = 0;
            using var reader = new StreamReader(
                stream,
                new UTF8Encoding(false, true),
                detectEncodingFromByteOrderMarks: true,
                leaveOpen: true);
            var buffer = new char[4096];
            while (reader.Read(buffer, 0, buffer.Length) > 0)
            {
                if (buffer.Any(x => x == '\0'))
                {
                    return false;
                }
            }

            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
        finally
        {
            stream.Position = 0;
        }
    }

    private static bool IsHeic(byte[] header)
    {
        if (Ascii(header, 4, 4) != "ftyp")
        {
            return false;
        }

        var brand = Ascii(header, 8, 4);
        return brand is "heic" or "heix" or "hevc" or "hevx" or "heim" or "heis" or "mif1" or "msf1";
    }

    private static bool IsIsoMedia(byte[] header, bool quickTime)
    {
        if (Ascii(header, 4, 4) != "ftyp")
        {
            return false;
        }

        var brand = Ascii(header, 8, 4);
        return quickTime
            ? brand is "qt  "
            : brand is "isom" or "iso2" or "mp41" or "mp42" or "avc1" or "M4V " or "M4A ";
    }

    private static string CanonicalContentType(string extension) => extension switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".webp" => "image/webp",
        ".heic" => "image/heic",
        ".pdf" => "application/pdf",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ".txt" => "text/plain; charset=utf-8",
        ".mp4" => "video/mp4",
        ".mov" => "video/quicktime",
        ".avi" => "video/x-msvideo",
        _ => "application/octet-stream"
    };

    private static string SafeFileName(string suppliedName)
    {
        var leaf = suppliedName.Replace('\\', '/').Split('/').LastOrDefault()?.Trim();
        if (string.IsNullOrWhiteSpace(leaf))
        {
            throw new InvalidOperationException("РќРµРґРѕРїСѓСЃС‚РёРјРѕРµ РёРјСЏ С„Р°Р№Р»Р°.");
        }

        if (leaf.Length <= 255)
        {
            return leaf;
        }

        var extension = Path.GetExtension(leaf);
        var stemLength = Math.Max(1, 255 - extension.Length);
        return leaf[..stemLength] + extension;
    }

    private static UploadKind KindFor(string extension) => extension switch
    {
        ".jpg" or ".jpeg" or ".png" or ".webp" or ".heic" => UploadKind.Image,
        ".pdf" or ".docx" or ".xlsx" or ".txt" => UploadKind.Document,
        ".mp4" or ".mov" or ".avi" => UploadKind.Video,
        _ => throw new InvalidOperationException("Неподдерживаемый тип файла.")
    };

    private static bool StartsWith(byte[] value, byte[] prefix) =>
        value.Length >= prefix.Length && value.AsSpan(0, prefix.Length).SequenceEqual(prefix);

    private static string Ascii(byte[] value, int offset, int count) =>
        value.Length >= offset + count ? Encoding.ASCII.GetString(value, offset, count) : string.Empty;
}

public enum UploadPurpose
{
    Chat,
    QA,
    Avatar,
    Knowledge
}

public enum UploadKind
{
    Image,
    Document,
    Video
}

public sealed record ValidatedUpload(
    Stream Content,
    string OriginalFileName,
    string Extension,
    string ContentType,
    UploadKind Kind,
    long Length) : IAsyncDisposable
{
    public ValueTask DisposeAsync() => Content.DisposeAsync();
}

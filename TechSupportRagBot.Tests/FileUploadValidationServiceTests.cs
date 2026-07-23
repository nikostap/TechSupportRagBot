using Microsoft.AspNetCore.Http;
using SkiaSharp;
using TechSupportRagBot.Services;

namespace TechSupportRagBot.Tests;

public sealed class FileUploadValidationServiceTests
{
    private readonly FileUploadValidationService _validator = new();

    [Fact]
    public async Task Valid_png_is_decoded_and_reencoded()
    {
        using var bitmap = new SKBitmap(2, 2);
        bitmap.Erase(SKColors.CornflowerBlue);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        await using var input = new MemoryStream(encoded.ToArray());
        var file = new FormFile(input, 0, input.Length, "file", "avatar.png")
        {
            Headers = new HeaderDictionary(),
            ContentType = "image/png"
        };

        await using var validated = await _validator.ValidateAsync(file, UploadPurpose.Avatar);

        Assert.Equal("image/png", validated.ContentType);
        Assert.Equal(UploadKind.Image, validated.Kind);
        Assert.True(validated.Length > 0);
    }

    [Fact]
    public async Task Html_disguised_as_png_is_rejected()
    {
        await using var input = new MemoryStream("<html><script>alert(1)</script></html>"u8.ToArray());
        var file = new FormFile(input, 0, input.Length, "file", "photo.png")
        {
            Headers = new HeaderDictionary(),
            ContentType = "image/png"
        };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _validator.ValidateAsync(file, UploadPurpose.Chat));
    }

    [Fact]
    public async Task Svg_is_rejected_even_with_image_mime()
    {
        await using var input = new MemoryStream("<svg xmlns=\"http://www.w3.org/2000/svg\"/>"u8.ToArray());
        var file = new FormFile(input, 0, input.Length, "file", "image.svg")
        {
            Headers = new HeaderDictionary(),
            ContentType = "image/svg+xml"
        };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _validator.ValidateAsync(file, UploadPurpose.Chat));
    }
}

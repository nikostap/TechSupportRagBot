using FastText.NetWrapper;
using Microsoft.Extensions.Options;

namespace TechSupportRagBot.Services;

public sealed class LanguageDetectionService : IDisposable
{
    private readonly IWebHostEnvironment _environment;
    private readonly FastTextLanguageOptions _options;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<LanguageDetectionService> _logger;
    private readonly object _sync = new();
    private FastTextWrapper? _model;
    private bool _loadAttempted;

    public LanguageDetectionService(
        IWebHostEnvironment environment,
        IOptions<FastTextLanguageOptions> options,
        ILoggerFactory loggerFactory,
        ILogger<LanguageDetectionService> logger)
    {
        _environment = environment;
        _options = options.Value;
        _loggerFactory = loggerFactory;
        _logger = logger;
    }

    public string? DetectLanguageCode(string? text, string? fallbackLanguage = null)
    {
        var fallbackCode = ChatTranslationService.LanguageToLibreTranslateCode(ChatTranslationService.NormalizeLanguage(fallbackLanguage));
        if (string.IsNullOrWhiteSpace(text))
        {
            return fallbackCode;
        }

        var normalizedText = NormalizeForFastText(text);
        if (CountLetters(normalizedText) < 2)
        {
            return fallbackCode;
        }

        var model = GetModel();
        if (model != null)
        {
            try
            {
                var prediction = model.PredictSingle(normalizedText);
                var code = NormalizeFastTextLabel(prediction.Label);
                if (!string.IsNullOrWhiteSpace(code)
                    && prediction.Probability >= _options.MinConfidence
                    && ChatTranslationService.LanguageToLibreTranslateCode(code) != null)
                {
                    return code;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "fastText language detection failed.");
            }
        }

        return ChatTranslationService.LanguageToLibreTranslateCode(ChatTranslationService.DetectMessageLanguage(text, fallbackLanguage))
            ?? fallbackCode;
    }

    private FastTextWrapper? GetModel()
    {
        if (_loadAttempted)
        {
            return _model;
        }

        lock (_sync)
        {
            if (_loadAttempted)
            {
                return _model;
            }

            _loadAttempted = true;
            var modelPath = ResolveModelPath(_options.ModelPath);
            if (!File.Exists(modelPath))
            {
                _logger.LogWarning("fastText language model not found: {ModelPath}", modelPath);
                return null;
            }

            try
            {
                _model = new FastTextWrapper(_loggerFactory);
                _model.LoadModel(modelPath);
                _logger.LogInformation("fastText language model loaded: {ModelPath}", modelPath);
            }
            catch (Exception ex)
            {
                _model?.Dispose();
                _model = null;
                _logger.LogWarning(ex, "Could not load fastText language model: {ModelPath}", modelPath);
            }

            return _model;
        }
    }

    private string ResolveModelPath(string modelPath)
    {
        if (Path.IsPathRooted(modelPath))
        {
            return modelPath;
        }

        return Path.Combine(_environment.ContentRootPath, modelPath);
    }

    private static string NormalizeForFastText(string text)
    {
        var normalized = string.Join(' ', text.Split(Array.Empty<char>(), StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= 1000 ? normalized : normalized[..1000];
    }

    private static int CountLetters(string text) => text.Count(char.IsLetter);

    private static string? NormalizeFastTextLabel(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return null;
        }

        var code = label
            .Replace("__label__", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim()
            .ToLowerInvariant();

        return code switch
        {
            "zh-yue" or "zh" => "zh",
            "he" or "iw" => "he",
            _ => code
        };
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _model?.Dispose();
            _model = null;
        }
    }
}

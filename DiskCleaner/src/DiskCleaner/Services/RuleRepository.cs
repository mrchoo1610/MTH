using System.Text.Json;
using System.Text.Json.Serialization;
using DiskCleaner.Core;
using DiskCleaner.Core.Models;

namespace DiskCleaner.Services;

public static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
}

public sealed class RuleRepository
{
    private readonly string _jsonPath;
    private readonly IEnvironment _env;
    private readonly ILogger _log;

    /// <summary>Các rule bị loại ở lần LoadAll gần nhất (kèm lý do).</summary>
    public IReadOnlyList<string> Rejected { get; private set; } = [];

    public RuleRepository(string jsonPath, IEnvironment? env = null, ILogger? log = null)
    {
        _jsonPath = jsonPath;
        _env = env ?? new SystemEnvironment();
        _log = log ?? NullLogger.Instance;
    }

    public IReadOnlyList<CleanRule> LoadAll()
    {
        var rejected = new List<string>();
        var parsed = new List<CleanRule>();
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(_jsonPath),
                new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (!doc.RootElement.TryGetProperty("rules", out var arr) || arr.ValueKind != JsonValueKind.Array)
                throw new JsonException("Thiếu mảng 'rules'");

            foreach (var el in arr.EnumerateArray())
            {
                try
                {
                    var rule = el.Deserialize<CleanRule>(JsonDefaults.Options);
                    if (rule != null) parsed.Add(rule);
                }
                catch (JsonException ex)
                {
                    rejected.Add($"Rule không đọc được: {ex.Message}");
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _log.Error($"Không đọc được {_jsonPath}", ex);
            rejected.Add($"Không đọc được file rules: {ex.Message}");
            Rejected = rejected;
            return [];
        }

        var result = RuleValidator.ValidateAll(parsed, _env);
        rejected.AddRange(result.Errors);
        foreach (var r in rejected) _log.Error("Loại rule: " + r);
        Rejected = rejected;
        return result.Valid;
    }
}

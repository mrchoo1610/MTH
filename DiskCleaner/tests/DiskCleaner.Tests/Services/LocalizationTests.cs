using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using DiskCleaner.Core.Models;
using DiskCleaner.Services;
using DiskCleaner.Tests.TestHelpers;

namespace DiskCleaner.Tests.Services;

public class LocalizationTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static string UiSrc => Path.Combine(AppContext.BaseDirectory, "UiSrc");

    private static Dictionary<string, string> Load(string file)
    {
        var doc = XDocument.Load(Path.Combine(UiSrc, "Localization", file));
        return doc.Root!.Elements().Where(e => e.Attribute(X + "Key") != null)
            .ToDictionary(e => e.Attribute(X + "Key")!.Value, e => e.Value);
    }

    [Fact]
    public void ViAndEn_HaveSameKeys_NoneEmpty()
    {
        var vi = Load("Strings.vi.xaml");
        var en = Load("Strings.en.xaml");
        Assert.Empty(vi.Keys.Except(en.Keys));
        Assert.Empty(en.Keys.Except(vi.Keys));
        Assert.DoesNotContain(vi, kv => string.IsNullOrWhiteSpace(kv.Value));
        Assert.DoesNotContain(en, kv => string.IsNullOrWhiteSpace(kv.Value));
    }

    [Fact]
    public void FormatPlaceholders_MatchBetweenLanguages()
    {
        var vi = Load("Strings.vi.xaml");
        var en = Load("Strings.en.xaml");
        foreach (var k in vi.Keys)
        {
            var a = Regex.Matches(vi[k], @"\{\d+\}").Select(m => m.Value).OrderBy(x => x);
            var b = Regex.Matches(en[k], @"\{\d+\}").Select(m => m.Value).OrderBy(x => x);
            Assert.True(a.SequenceEqual(b), $"Placeholder khac nhau o key {k}");
        }
    }

    [Fact]
    public void EveryRule_HasEnglishTexts()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Rules", "rules.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        foreach (var r in doc.RootElement.GetProperty("rules").EnumerateArray())
        {
            var id = r.GetProperty("id").GetString();
            Assert.True(r.TryGetProperty("nameEn", out var n) && !string.IsNullOrWhiteSpace(n.GetString()), $"{id}: thieu nameEn");
            Assert.True(r.TryGetProperty("descriptionEn", out var d) && !string.IsNullOrWhiteSpace(d.GetString()), $"{id}: thieu descriptionEn");
            if (r.TryGetProperty("warning", out _))
                Assert.True(r.TryGetProperty("warningEn", out var w) && !string.IsNullOrWhiteSpace(w.GetString()), $"{id}: thieu warningEn");
        }
    }

    [Fact]
    public void CleanRule_GettersFallBackToVietnamese()
    {
        var rule = new CleanRule
        {
            Id = "x", Name = "Ten", Description = "MoTa", Category = RuleCategory.System,
            Action = RuleAction.DeleteFiles, Warning = "CanhBao"
        };
        Assert.Equal("Ten", rule.GetName(AppLanguage.En));
        Assert.Equal("MoTa", rule.GetDescription(AppLanguage.En));
        Assert.Equal("CanhBao", rule.GetWarning(AppLanguage.En));
        var en = rule with { NameEn = "Name", DescriptionEn = "Desc", WarningEn = "Warn" };
        Assert.Equal("Name", en.GetName(AppLanguage.En));
        Assert.Equal("Ten", en.GetName(AppLanguage.Vi));
        Assert.Equal("Warn", en.GetWarning(AppLanguage.En));
    }

    [Fact]
    public void XamlAndCode_OnlyUseExistingKeys()
    {
        var keys = Load("Strings.vi.xaml").Keys.ToHashSet();
        var used = new List<(string File, string Key)>();
        foreach (var f in Directory.EnumerateFiles(UiSrc, "*.xaml", SearchOption.AllDirectories)
                     .Where(f => !Path.GetFileName(f).StartsWith("Strings.")))
        {
            var t = File.ReadAllText(f);
            foreach (Match m in Regex.Matches(t, @"\{DynamicResource ([A-Za-z0-9.]+)\}")) used.Add((f, m.Groups[1].Value));
            foreach (Match m in Regex.Matches(t, @"HeaderKey=""([A-Za-z0-9.]+)""")) used.Add((f, m.Groups[1].Value));
        }
        foreach (var f in Directory.EnumerateFiles(UiSrc, "*.cs", SearchOption.AllDirectories))
            foreach (Match m in Regex.Matches(File.ReadAllText(f), @"\b(?:Get|Format)\(""([A-Za-z0-9]+\.[A-Za-z0-9.]*[A-Za-z0-9])"""))
                used.Add((f, m.Groups[1].Value));

        Assert.NotEmpty(used);
        var missing = used.Where(u => !keys.Contains(u.Key)).Select(u => $"{Path.GetFileName(u.File)}: {u.Key}").ToList();
        Assert.Empty(missing);
    }

    [Fact]
    public void NoVietnameseText_InXamlOutsideViStrings()
    {
        var viet = new Regex("[àáạảãâầấậẩẫăằắặẳẵèéẹẻẽêềếệểễìíịỉĩòóọỏõôồốộổỗơờớợởỡùúụủũưừứựửữỳýỵỷỹđ]", RegexOptions.IgnoreCase);
        foreach (var f in Directory.EnumerateFiles(UiSrc, "*.xaml", SearchOption.AllDirectories)
                     .Where(f => Path.GetFileName(f) != "Strings.vi.xaml"))
            Assert.False(viet.IsMatch(Regex.Replace(File.ReadAllText(f), "<!--.*?-->", "", RegexOptions.Singleline)), $"Chu co dau tieng Viet hardcode trong {Path.GetFileName(f)}");
    }

    private static LocalizationService Create(SettingsService s) => new(s, l => l == AppLanguage.Vi
        ? new Dictionary<string, string> { ["A"] = "xin chao {0}" }
        : new Dictionary<string, string> { ["A"] = "hello {0}" });

    [Fact]
    public void Service_GetFormatMissingKey_AndSwitchRaisesEvent()
    {
        using var t = new TempTree();
        var settings = new SettingsService(t.P("s.json"), new CultureInfo("vi-VN"));
        var loc = Create(settings);
        Assert.Equal(AppLanguage.Vi, loc.Current);
        Assert.Equal("xin chao Bob", loc.Format("A", "Bob"));
        Assert.Equal("[Nope]", loc.Get("Nope"));

        int raised = 0;
        loc.LanguageChanged += (_, _) => raised++;
        loc.SetLanguage(AppLanguage.En);
        Assert.Equal(1, raised);
        Assert.Equal("hello Bob", loc.Format("A", "Bob"));

        var again = Create(new SettingsService(t.P("s.json"), new CultureInfo("vi-VN")));
        Assert.Equal(AppLanguage.En, again.Current);
    }

    [Theory]
    [InlineData("vi-VN", AppLanguage.Vi)]
    [InlineData("en-US", AppLanguage.En)]
    [InlineData("fr-FR", AppLanguage.En)]
    public void Settings_FirstRun_UsesUiCulture(string culture, AppLanguage expected)
    {
        using var t = new TempTree();
        Assert.Equal(expected, new SettingsService(t.P("none.json"), new CultureInfo(culture)).LoadLanguage());
    }
}

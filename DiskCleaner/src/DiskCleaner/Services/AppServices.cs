using DiskCleaner.Core;
using DiskCleaner.Core.Models;

namespace DiskCleaner.Services;

/// <summary>Composition root đơn giản: tạo và giữ các service dùng chung.</summary>
public sealed class AppServices
{
    public IEnvironment Env { get; }
    public SafetyGuardContext Guard { get; }
    public ICommandRunner Runner { get; }
    public IRecycleBinService RecycleBin { get; }
    public ElevationService Elevation { get; }
    public DriveInfoService Drives { get; }
    public FileSystemScanner Scanner { get; }
    public FolderAnalyzer Analyzer { get; }
    public LargeFileFinder LargeFiles { get; }
    public SettingsService Settings { get; }
    public LocalizationService Loc { get; }
    public ScanResultStore ScanResults { get; }
    /// <summary>Rule dựng sẵn từ rules.json (không gồm rule tùy chỉnh).</summary>
    public IReadOnlyList<CleanRule> Rules { get; }
    public CustomRuleStore CustomRules { get; }
    public IReadOnlyList<string> RejectedRules { get; }

    /// <summary>Rule dựng sẵn + rule tùy chỉnh hiện có.</summary>
    public IReadOnlyList<CleanRule> AllRules => [.. Rules, .. CustomRules.Current];

    public static string RulesPath => Path.Combine(AppContext.BaseDirectory, "Rules", "rules.json");

    public AppServices()
    {
        Settings = new SettingsService();
        Loc = LocalizationService.CreateForApplication(Settings);
        ScanResults = new ScanResultStore();
        Env = new SystemEnvironment();
        Guard = new SafetyGuardContext(Env);
        Runner = new CommandRunner();
        RecycleBin = new RecycleBinService();
        Elevation = new ElevationService();
        Drives = new DriveInfoService();
        Scanner = new FileSystemScanner(Env, Runner, RecycleBin);
        Analyzer = new FolderAnalyzer();
        LargeFiles = new LargeFileFinder();

        var repo = new RuleRepository(RulesPath, Env, AppLog.Instance);
        Rules = repo.LoadAll();
        RejectedRules = repo.Rejected;

        CustomRules = new CustomRuleStore(CustomRuleStore.DefaultPath, Env, AppLog.Instance, Rules);
        CustomRules.LoadAll();
    }
}

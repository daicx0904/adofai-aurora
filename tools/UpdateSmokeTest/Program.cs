using System.IO;
using AdofaiModManager.Models;
using AdofaiModManager.Services;

// ---- 版本比较自检（回归：26w40c → 26w42a 曾因只取第一段数字而漏报）----
var versionChecks = new (string Local, string Remote, bool ExpectNewer)[]
{
    ("26w40c", "26w42a", true),
    ("26w42a", "26w40c", false),
    ("26w40c", "26w40c", false),
    ("26w40b", "26w40c", true),
    ("2.4.1", "2.4.2", true),
    ("2.4.1", "2.4.1", false),
    ("1.9.0", "1.10.0", true),
};
var versionBad = 0;
foreach (var (local, remote, expect) in versionChecks)
{
    var actual = VersionScheme.IsRemoteNewer(local, remote);
    var ok = actual == expect;
    if (!ok)
    {
        versionBad++;
    }

    Console.WriteLine($"  版本比较 {local,-10} vs {remote,-10} 期望 {(expect ? "有更新" : "无更新")} 实得 {(actual ? "有更新" : "无更新")} {(ok ? "OK" : "FAIL")}");
}

Console.WriteLine(versionBad == 0
    ? $"版本比较自检通过（{versionChecks.Length} 项）"
    : $"警告：版本比较自检失败 {versionBad} 项");
Console.WriteLine();

var gamePath = args.Length > 0 ? args[0] : @"D:\SteamLibrary\steamapps\common\A Dance of Fire and Ice";

var modService = new ModService(gamePath);

if (!Directory.Exists(modService.ModsPath))
{
    Console.WriteLine($"找不到 Mods 目录：{modService.ModsPath}");
    return 1;
}

var mods = modService.Scan();
Console.WriteLine($"已安装 {mods.Count} 个 mod，开始逐个解析更新源并检查…");
Console.WriteLine(new string('-', 100));

var store = new UpdateSourceStore(Path.Combine(Path.GetTempPath(), "amm-test-update-sources.json"));
var service = new GitHubUpdateService(store);

var updatable = 0;
var failed = 0;
var noSource = 0;

foreach (var mod in mods)
{
    var source = service.ResolveSource(mod);

    Console.WriteLine($"{mod.DisplayName}  (Id={mod.Id}, v{mod.Version})");
    Console.WriteLine($"    更新源: {source.Description}");

    if (source.Kind == UpdateSourceKind.None)
    {
        noSource++;
        Console.WriteLine("    结果  : 无更新源（需手动绑定 GitHub）");
        continue;
    }

    var result = await service.CheckAsync(source, mod.Id, mod.Version);

    if (!result.Success)
    {
        failed++;
        Console.WriteLine($"    结果  : ❌ {result.Message}");
    }
    else if (result.UpdateAvailable)
    {
        updatable++;
        Console.WriteLine($"    结果  : ⬆ 可更新 {mod.Version} → {result.RemoteVersion}");
        Console.WriteLine($"    下载  : {result.FileName ?? result.DownloadUrl}");
    }
    else
    {
        Console.WriteLine($"    结果  : ✓ 已是最新（{result.RemoteVersion ?? mod.Version}）");
    }
}

Console.WriteLine(new string('-', 100));
Console.WriteLine($"汇总：{mods.Count} 个 mod，可更新 {updatable}，检查失败 {failed}，无更新源 {noSource}");
return 0;

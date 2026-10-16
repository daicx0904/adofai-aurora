using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace AdofaiModManager.Services.Sources;

/// <summary>
/// modrift.org 适配器（多游戏站，这里固定只取 <c>game=adofai</c>）。
///
/// 免 key、只读接口全部公开，国内可直连。接的时候按站长给的接口文档注意了这几件事：
///  · 列表接口**不带版本号**，要再取一次 versions；
///  · "最新版本"可能**没有文件**（站点允许发空版本），必须跳过，否则下载必失败；
///  · 一个版本可能有**多个文件**（按游戏版本分流，例：+adofai_v2 / +adofai_v3），
///    下载地址也不同（靠 gameVersion 参数选文件），所以要按当前游戏版本挑；
///  · 下载地址形如 <c>/v1/mod/{slug}@{version}:download?loader=..&amp;gameVersion=..</c>，直接拼站点前缀用即可；
///  · <c>q</c> 是"未转义正则"（文档「已知问题」#12），搜索词先 Regex.Escape；
///  · 图标不是直链：<c>iconUrl</c> 只是文件名，真正的地址是 <c>/api/mods/{_id}/icon/raw</c>；部分 mod 没有图标。
/// </summary>
public sealed class ModriftSource : IRemoteSource
{
    private const string BaseUrl = "https://modrift.org";
    private const string Game = "adofai";

    private readonly HttpClient _http;

    public ModriftSource(HttpClient? http = null) => _http = http ?? SourceHttp.CreateClient();

    /// <summary>
    /// 当前游戏版本（用于挑"匹配本机游戏版本"的那个文件）。
    /// 由上层注入（主程序在启动时挂上）；拿不到就只按 UnityModManager / 下载量挑。
    /// </summary>
    public static Func<string?>? GameVersionProvider { get; set; }

    private static string? GameVersion => GameVersionProvider?.Invoke();

    public string Id => "modrift";

    public string DisplayName => "modrift";

    public bool RequiresKey => false;

    /// <summary>站点没有固定分类清单，界面就不显示「类型」筛选。</summary>
    public IReadOnlyList<string> Categories => [];

    public string? ModPageUrl(string slug) =>
        string.IsNullOrWhiteSpace(slug) ? null : $"{BaseUrl}/mod/{Uri.EscapeDataString(slug)}";

    public async Task<RemoteModPage> GetModsAsync(RemoteModQuery query, CancellationToken ct = default)
    {
        var page = Math.Max(1, query.Page);
        var size = Math.Clamp(query.PageSize, 1, 100);

        var sort = query.Sort switch
        {
            RemoteSort.Downloads => "downloads",
            RemoteSort.Name => "title",
            _ => "updated",                 // 收藏量没有对应排序，取回来再本地排
        };

        var url = $"{BaseUrl}/api/mods?game={Game}&page={page}&limit={size}&sort={sort}";

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // 站点的 q 是按正则匹配的，先转义，避免特殊字符造成怪匹配 / 慢查询
            url += "&q=" + Uri.EscapeDataString(Regex.Escape(query.Search.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(query.Category) && query.Category != "all")
        {
            url += "&category=" + Uri.EscapeDataString(query.Category);
        }

        using var doc = await SourceHttp.GetJsonCachedAsync(_http, url, SourceHttp.ListTtl, ct);
        var root = doc.RootElement;

        var total = (int)Math.Clamp(Json.Long(root, "total"), 0, int.MaxValue);
        var items = new List<RemoteMod>();

        if (root.TryGetProperty("mods", out var mods) && mods.ValueKind == JsonValueKind.Array)
        {
            // 接口不带版本号：每个 mod 单独取一次（条数很少，并行 + 走缓存）
            var tasks = mods.EnumerateArray().Select(m => MapModAsync(m, ct)).ToList();

            foreach (var mapped in await Task.WhenAll(tasks))
            {
                if (mapped is not null)
                {
                    items.Add(mapped);
                }
            }
        }

        if (query.Sort == RemoteSort.Favorites)
        {
            items = items.OrderByDescending(m => m.Likes).ThenByDescending(m => m.UpdatedAt).ToList();
        }

        return new RemoteModPage(items, total, page, size);
    }

    public async Task<RemoteModDetail?> GetModDetailAsync(string slug, CancellationToken ct = default)
    {
        using var doc = await SourceHttp.GetJsonCachedAsync(
            _http,
            $"{BaseUrl}/api/mods/slug/{Uri.EscapeDataString(slug)}?type=mod",
            SourceHttp.DetailTtl,
            ct);

        var root = doc.RootElement;

        if (root.ValueKind != JsonValueKind.Object || Json.Text(root, "_id").Length == 0)
        {
            return null;
        }

        var versions = await FetchVersionsAsync(slug, ct);
        var mod = BuildMod(root, versions);

        if (mod is null)
        {
            return null;
        }

        var description = Json.Text(root, "description");
        if (string.IsNullOrWhiteSpace(description))
        {
            description = Json.Text(root, "readme");
        }

        if (description.Length > 6000)
        {
            description = description[..6000] + "…";
        }

        return new RemoteModDetail(mod, description, versions.Select(v => ToRemoteVersion(v)).ToList());
    }

    public async Task<RemoteDownload?> ResolveDownloadAsync(
        string slug,
        RemoteVersion version,
        string platform,
        CancellationToken ct = default)
    {
        // 版本对象里不带链接（一个版本可能对应多个文件），所以现场解析一次；接口有缓存，很便宜
        var versions = await FetchVersionsAsync(slug, ct);
        var match = versions.FirstOrDefault(v => v.Version.Equals(version.VersionId, StringComparison.OrdinalIgnoreCase));

        if (match is null)
        {
            return null;
        }

        var file = PickFile(match.Files, GameVersion);
        if (file is null)
        {
            return null;
        }

        var name = string.IsNullOrWhiteSpace(file.Name) ? $"{slug}-{match.Version}.zip" : file.Name;
        return RemoteDownload.Unverified(file.Url, name);
    }

    // ---------------- 内部：抓 + 映射 ----------------

    private async Task<RemoteMod?> MapModAsync(JsonElement mod, CancellationToken ct)
    {
        var versions = await FetchVersionsAsync(Json.Text(mod, "slug"), ct);
        return BuildMod(mod, versions);
    }

    private static RemoteMod? BuildMod(JsonElement mod, IReadOnlyList<ModriftVersion> versions)
    {
        var id = Json.Text(mod, "_id");
        var name = Json.Text(mod, "title");

        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var chosen = PickVersion(versions);
        var file = chosen is null ? null : PickFile(chosen.Files, GameVersion);
        var summary = Json.Text(mod, "summary");
        var description = Json.Text(mod, "description");
        var iconName = Json.Text(mod, "iconUrl");

        var homepage = Json.Text(mod, "sourceUrl");
        if (string.IsNullOrWhiteSpace(homepage))
        {
            homepage = Json.Text(mod, "homepageUrl");
        }

        return new RemoteMod(
            SourceId: "modrift",
            Id: id,
            Slug: Json.Text(mod, "slug"),
            Name: name,
            Authors: AuthorNames(mod),
            Summary: summary.Length > 0 ? summary : Json.FirstLine(description),
            // 图标不是直链：只有存在 iconUrl 文件名时才给这个接口地址
            IconUrl: iconName.Length > 0 && id.Length > 0 ? $"{BaseUrl}/api/mods/{id}/icon/raw" : null,
            Categories: Json.Strings(mod, "categories", "name"),
            LatestVersion: chosen?.Version ?? string.Empty,
            GameVersion: file?.GameConstraint,
            Downloads: Json.Long(mod, "downloads"),
            Likes: Json.Long(mod, "favorites"),
            HomepageUrl: homepage.Length > 0 ? homepage : null,
            UpdatedAt: Json.DateTime(mod, "updatedAt"),
            Loader: PickLoader(chosen?.Files ?? []));
    }

    /// <summary>
    /// 取版本列表；**丢掉没有文件的版本**（站点允许发空版本，选了也装不了）。
    /// 单个 mod 取不到版本（例如有的条目压根没有版本、接口 404）时返回空列表，不影响整页。
    /// </summary>
    private async Task<List<ModriftVersion>> FetchVersionsAsync(string slug, CancellationToken ct)
    {
        var result = new List<ModriftVersion>();

        if (string.IsNullOrWhiteSpace(slug))
        {
            return result;
        }

        JsonDocument doc;
        try
        {
            doc = await SourceHttp.GetJsonCachedAsync(
                _http,
                $"{BaseUrl}/api/mods/{Uri.EscapeDataString(slug)}/versions?type=mod",
                SourceHttp.ListTtl,
                ct);
        }
        catch (Exception)
        {
            // 单个 mod 取不到版本（例如该条目压根没有版本、接口 404）就当作"没有版本"，
            // 不能把整页列表弄挂。
            return result;
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Array)
            {
                return result;
            }

        foreach (var version in root.EnumerateArray())
        {
            var files = new List<ModriftFile>();

            if (version.TryGetProperty("files", out var fileArray) && fileArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var file in fileArray.EnumerateArray())
                {
                    var url = FirstDownloadUrl(file);
                    if (string.IsNullOrWhiteSpace(url))
                    {
                        continue;
                    }

                    files.Add(new ModriftFile(
                        Name: Json.Text(file, "originalName"),
                        Url: url,
                        Downloads: Json.Long(file, "downloads"),
                        Loaders: Json.Strings(file, "loaders", "name"),
                        GameConstraint: FirstGameConstraint(file)));
                }
            }

            if (files.Count == 0)
            {
                continue;
            }

            result.Add(new ModriftVersion(
                Version: Json.Text(version, "version"),
                IsLatest: Json.Bool(version, "isLatest"),
                IsBeta: IsBetaType(Json.Text(version, "versionType")),
                Changelog: Json.Text(version, "changelog"),
                Files: files));
            }

            return result;
        }
    }

    private static RemoteVersion ToRemoteVersion(ModriftVersion version)
    {
        var file = PickFile(version.Files, GameVersion);

        return new RemoteVersion(
            VersionId: version.Version,
            Changelog: version.Changelog,
            GameVersion: file?.GameConstraint,
            IsBeta: version.IsBeta,
            Platforms: ["windows"],
            // 这里不填直链：一个版本可能对应多个文件，等安装时再按游戏版本挑（接口有缓存）
            DirectUrl: null);
    }

    /// <summary>最新版优先取站点标记的 isLatest；否则取列表第一条（接口按 createdAt 降序）。</summary>
    private static ModriftVersion? PickVersion(IReadOnlyList<ModriftVersion> versions)
    {
        if (versions.Count == 0)
        {
            return null;
        }

        return versions.FirstOrDefault(v => v.IsLatest) ?? versions[0];
    }

    /// <summary>
    /// 挑一个文件装：优先支持 UnityModManager 的；再优先能匹配当前游戏版本的；再优先下载量高的。
    /// </summary>
    private static ModriftFile? PickFile(IReadOnlyList<ModriftFile> files, string? gameVersion)
    {
        if (files.Count == 0)
        {
            return null;
        }

        var umm = files.Where(f => HasLoader(f, "unitymodmanager")).ToList();
        var pool = umm.Count > 0 ? umm : files.ToList();

        var matched = pool.Where(f => MatchesGameVersion(f.GameConstraint, gameVersion)).ToList();
        if (matched.Count > 0)
        {
            return matched.OrderByDescending(f => f.Downloads).First();
        }

        return pool.OrderByDescending(f => f.Downloads).First();
    }

    private static ModLoader PickLoader(IReadOnlyList<ModriftFile> files)
    {
        if (files.Any(f => HasLoader(f, "unitymodmanager")))
        {
            return ModLoader.Umm;
        }

        if (files.Any(f => HasLoader(f, "melonloader")))
        {
            return ModLoader.MelonLoader;
        }

        return ModLoader.Unknown;
    }

    private static bool HasLoader(ModriftFile file, string name) =>
        file.Loaders.Any(l => l.Contains(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>匹配 <c>&gt;=2.10.0</c> / <c>[2.9.5,2.9.8]</c> 这类游戏版本约束。</summary>
    private static bool MatchesGameVersion(string? constraint, string? gameVersion)
    {
        if (string.IsNullOrWhiteSpace(constraint) || string.IsNullOrWhiteSpace(gameVersion))
        {
            return false;
        }

        var text = constraint.Trim();

        var match = Regex.Match(text, @"^>=\s*(.+)$");
        if (match.Success)
        {
            return CompareGameVersion(gameVersion, match.Groups[1].Value.Trim()) >= 0;
        }

        match = Regex.Match(text, @"^>\s*(.+)$");
        if (match.Success)
        {
            return CompareGameVersion(gameVersion, match.Groups[1].Value.Trim()) > 0;
        }

        match = Regex.Match(text, @"^<=\s*(.+)$");
        if (match.Success)
        {
            return CompareGameVersion(gameVersion, match.Groups[1].Value.Trim()) <= 0;
        }

        match = Regex.Match(text, @"^<\s*(.+)$");
        if (match.Success)
        {
            return CompareGameVersion(gameVersion, match.Groups[1].Value.Trim()) < 0;
        }

        match = Regex.Match(text, @"^[\[\(]\s*([^,]+?)\s*,\s*([^\]\)]+?)\s*[\]\)]$");
        if (match.Success)
        {
            return CompareGameVersion(gameVersion, match.Groups[1].Value.Trim()) >= 0
                   && CompareGameVersion(gameVersion, match.Groups[2].Value.Trim()) <= 0;
        }

        return false;
    }

    /// <summary>
    /// 游戏版本比较（只处理 2.10.0 这种点分数字，够用；不引入其它依赖，方便冒烟测试单编这个文件）。
    /// </summary>
    private static int CompareGameVersion(string? a, string? b)
    {
        static int[] Parts(string? value) =>
            (value ?? string.Empty)
                .Split('.', StringSplitOptions.RemoveEmptyEntries)
                .Select(part =>
                {
                    var digits = new string(part.TakeWhile(char.IsDigit).ToArray());
                    return int.TryParse(digits, out var number) ? number : 0;
                })
                .ToArray();

        var left = Parts(a);
        var right = Parts(b);
        var count = Math.Max(left.Length, right.Length);

        for (var i = 0; i < count; i++)
        {
            var l = i < left.Length ? left[i] : 0;
            var r = i < right.Length ? right[i] : 0;

            if (l != r)
            {
                return l.CompareTo(r);
            }
        }

        return 0;
    }

    private static string FirstGameConstraint(JsonElement file)
    {
        if (file.TryGetProperty("supportedGames", out var games) && games.ValueKind == JsonValueKind.Array)
        {
            foreach (var game in games.EnumerateArray())
            {
                var version = Json.Text(game, "version");
                if (!string.IsNullOrWhiteSpace(version))
                {
                    return version.Trim();
                }
            }
        }

        return string.Empty;
    }

    /// <summary>下载地址：多个时优先支持 UMM 的那个。</summary>
    private static string FirstDownloadUrl(JsonElement file)
    {
        if (!file.TryGetProperty("downloadUrls", out var urls) || urls.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        string? first = null;

        foreach (var item in urls.EnumerateArray())
        {
            var url = Json.Text(item, "url");
            if (string.IsNullOrWhiteSpace(url))
            {
                continue;
            }

            if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                first ??= url;
                if (Json.Text(item, "loader").Contains("unitymodmanager", StringComparison.OrdinalIgnoreCase))
                {
                    return url;
                }

                continue;
            }

            var absolute = BaseUrl + url;
            first ??= absolute;

            if (Json.Text(item, "loader").Contains("unitymodmanager", StringComparison.OrdinalIgnoreCase))
            {
                return absolute;
            }
        }

        return first ?? string.Empty;
    }

    private static string AuthorNames(JsonElement mod)
    {
        var names = new List<string>();

        if (mod.TryGetProperty("authors", out var authors) && authors.ValueKind == JsonValueKind.Array)
        {
            foreach (var author in authors.EnumerateArray())
            {
                var name = Json.Text(author, "username");

                if (name.Length == 0 &&
                    author.TryGetProperty("user", out var user) &&
                    user.ValueKind == JsonValueKind.Object)
                {
                    name = Json.Text(user, "username");
                }

                if (name.Length == 0)
                {
                    name = Json.Text(author, "id");
                }

                if (name.Length > 0)
                {
                    names.Add(name);
                }
            }
        }

        return string.Join("、", names);
    }

    private static bool IsBetaType(string type) =>
        type.Equals("beta", StringComparison.OrdinalIgnoreCase)
        || type.Equals("alpha", StringComparison.OrdinalIgnoreCase)
        || type.Equals("prerelease", StringComparison.OrdinalIgnoreCase)
        || type.Equals("pre-release", StringComparison.OrdinalIgnoreCase)
        || type.Equals("nightly", StringComparison.OrdinalIgnoreCase)
        || type.Equals("snapshot", StringComparison.OrdinalIgnoreCase)
        || type.Equals("rc", StringComparison.OrdinalIgnoreCase);

    private sealed record ModriftVersion(
        string Version,
        bool IsLatest,
        bool IsBeta,
        string Changelog,
        IReadOnlyList<ModriftFile> Files);

    private sealed record ModriftFile(
        string Name,
        string Url,
        long Downloads,
        IReadOnlyList<string> Loaders,
        string GameConstraint);
}

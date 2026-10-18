using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace AdofaiModManager.Services;

/// <summary>
/// 尝试从 Unity 的 globalgamemanagers 里读出游戏版本（bundleVersion）。
/// 读不到也没关系，用户可以在设置里手动填写。
/// </summary>
public static class GameVersionReader
{
    private static readonly Regex VersionPattern =
        new(@"(?:[^0-9])(\d{1,2})\.(\d{1,2})\.(\d{1,2})(?:[^0-9]|$)", RegexOptions.Compiled);

    private static readonly object CacheLock = new();

    // globalgamemanagers 有好几 MB，而「已安装」页每次刷新都要问一次游戏版本，
    // 所以按"路径 + 修改时间 + 大小"缓存，游戏更新后文件变了会自动重读。
    private static (string File, DateTime WriteTime, long Length, string? Version)? _cache;

    public static string? TryRead(string gamePath)
    {
        try
        {
            var dataFolder = Path.GetFileNameWithoutExtension(SteamGameLocator.GameExeName) + "_Data";
            var file = Path.Combine(gamePath, dataFolder, "globalgamemanagers");
            var info = new FileInfo(file);
            if (!info.Exists)
            {
                return null;
            }

            lock (CacheLock)
            {
                if (_cache is { } hit &&
                    string.Equals(hit.File, file, StringComparison.OrdinalIgnoreCase) &&
                    hit.WriteTime == info.LastWriteTimeUtc &&
                    hit.Length == info.Length)
                {
                    return hit.Version;
                }
            }

            var version = Parse(File.ReadAllBytes(file));

            lock (CacheLock)
            {
                _cache = (file, info.LastWriteTimeUtc, info.Length, version);
            }

            return version;
        }
        catch
        {
            // 忽略
        }

        return null;
    }

    private static string? Parse(byte[] bytes)
    {
        var text = Encoding.ASCII.GetString(bytes);

        // 组件限制为 1~2 位，可自动排除 Unity 版本（6000.x）与年份
        var match = VersionPattern.Match(text);
        return match.Success
            ? $"{match.Groups[1].Value}.{match.Groups[2].Value}.{match.Groups[3].Value}"
            : null;
    }
}

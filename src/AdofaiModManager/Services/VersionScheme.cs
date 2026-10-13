using System.Text;
using System.Text.RegularExpressions;

namespace AdofaiModManager.Services;

/// <summary>
/// 版本号"写法"的判断与比较 —— 用来处理作者在两边（mod 内的 Info.json / 资源站 / GitHub）
/// 用了不同格式的版本号时的更新判断，避免误报"有更新"，也避免漏报。
///
/// 例：本地 Info.json 写的是 26w40c（年份+周+修订字母），资源站上填的是 26.5.1（三段数字）。
/// 这种直接逐段比较会得出"资源站更新"的假结论。
/// </summary>
public static class VersionScheme
{
    /// <summary>两边是否是同一套写法（判断"字母是否紧贴数字"）。</summary>
    public static bool Same(string? a, string? b) => HasGluedDigitLetter(a) == HasGluedDigitLetter(b);

    /// <summary>字母紧贴数字：26w40c → true；26.5.1、26.5 Alpha、1.0.0-beta、2.0.r125 → false。</summary>
    public static bool HasGluedDigitLetter(string? version) =>
        !string.IsNullOrWhiteSpace(version) && Regex.IsMatch(version, @"[0-9][A-Za-z]");

    /// <summary>只比较第一个数字段（写法不同时的兜底，宁可少报也不误报）。</summary>
    public static int CompareFirstNumber(string? a, string? b)
    {
        static int FirstNumber(string? version)
        {
            var match = Regex.Match(version ?? string.Empty, @"\d+");
            return match.Success && int.TryParse(match.Value, out var value) ? value : 0;
        }

        return FirstNumber(a).CompareTo(FirstNumber(b));
    }

    /// <summary>
    /// 版本号比较（mod 用）。
    ///
    /// 「年份+周」这一套写法（26w40c / 26w42a）里，字母是修订、数字里夹着周数，
    /// 旧做法只取第一个数字段会漏报更新，所以这套写法改用"逐片段比较"。
    /// 其余写法保持原有行为：写法相同逐段比较，写法不同只比第一段。
    /// </summary>
    public static int Compare(string? a, string? b)
    {
        if (HasGluedDigitLetter(a) && HasGluedDigitLetter(b))
        {
            return CompareTokenized(a, b);
        }

        return Same(a, b)
            ? KernelService.CompareVersions(a, b)
            : CompareFirstNumber(a, b);
    }

    /// <summary>
    /// 判断"远端是否比本地新"（纯字符串判断，不发网络请求，适合列表里批量使用）。
    /// </summary>
    public static bool IsRemoteNewer(string? local, string? remote)
    {
        if (string.IsNullOrWhiteSpace(local) || string.IsNullOrWhiteSpace(remote))
        {
            return false;
        }

        return Compare(remote, local) > 0;
    }

    /// <summary>
    /// 把版本号切成「连续数字」与「连续字母」的片段后逐段比较。
    /// 例：26w40c → [26][w][40][c]，与 26w42a → [26][w][42][a] 比到第三段得出 42 &gt; 40。
    /// 片段先用完的一方视为更小（例：26w40 &lt; 26w40c，1.0 &lt; 1.0.1）。
    /// </summary>
    private static int CompareTokenized(string? a, string? b)
    {
        var left = Tokenize(a);
        var right = Tokenize(b);
        var count = Math.Max(left.Count, right.Count);

        for (var i = 0; i < count; i++)
        {
            var hasLeft = i < left.Count;
            var hasRight = i < right.Count;

            if (!hasLeft)
            {
                return -1;
            }

            if (!hasRight)
            {
                return 1;
            }

            var compare = CompareToken(left[i], right[i]);
            if (compare != 0)
            {
                return compare;
            }
        }

        return 0;
    }

    private static int CompareToken(string a, string b)
    {
        var aIsNumber = a.All(char.IsDigit);
        var bIsNumber = b.All(char.IsDigit);

        if (aIsNumber && bIsNumber)
        {
            if (long.TryParse(a, out var av) && long.TryParse(b, out var bv))
            {
                return av.CompareTo(bv);
            }

            // 超长数字：先比位数再比字典序
            return a.Length != b.Length ? a.Length.CompareTo(b.Length) : string.CompareOrdinal(a, b);
        }

        // 数字段排在字母段之前（同一套写法两端通常类型一致，这条只是兜底）
        if (aIsNumber != bIsNumber)
        {
            return aIsNumber ? -1 : 1;
        }

        var ignoreCase = string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
        return ignoreCase != 0 ? ignoreCase : string.CompareOrdinal(a, b);
    }

    private static List<string> Tokenize(string? version)
    {
        var tokens = new List<string>();

        if (string.IsNullOrWhiteSpace(version))
        {
            return tokens;
        }

        var current = new StringBuilder();
        bool? digits = null;

        foreach (var c in version)
        {
            if (!char.IsLetterOrDigit(c))
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                    digits = null;
                }

                continue;
            }

            var isDigit = char.IsDigit(c);

            if (digits is null)
            {
                digits = isDigit;
            }
            else if (digits != isDigit)
            {
                tokens.Add(current.ToString());
                current.Clear();
                digits = isDigit;
            }

            current.Append(c);
        }

        if (current.Length > 0)
        {
            tokens.Add(current.ToString());
        }

        return tokens;
    }
}

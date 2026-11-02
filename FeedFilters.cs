using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Alife.Function.FunctionCaller;

namespace presents.FeedScroll;

/// <summary>
/// 内容屏蔽：吧名黑名单 + 标签/关键词黑名单。
/// 配置里的清单和主人/角色后来追加的清单合并生效；列表先过滤，正文再兜底。
/// </summary>
public partial class FeedScrollModule
{
    static IEnumerable<string> SplitTerms(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<string>();
        return text.Split(new[] { ',', '，', ';', '；', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => x.Length > 0);
    }

    public List<string> EffectiveBlockedForums()
    {
        var list = new List<string>();
        void Add(string? term)
        {
            string value = NormalizeForum(term);
            if (value.Length < 2) return;
            if (list.Any(x => string.Equals(x, value, StringComparison.OrdinalIgnoreCase))) return;
            list.Add(value);
        }
        foreach (string term in SplitTerms(Configuration.BlockedForums)) Add(term);
        foreach (string term in state.BlockedForums) Add(term);
        // 放行名单只作用于运行时，不改插件配置；发版默认值保持不变
        return list
            .Where(x => !state.AllowedForums.Any(a => string.Equals(NormalizeForum(a), x, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    public List<string> EffectiveBlockedKeywords()
    {
        var list = new List<string>();
        void Add(string? term)
        {
            string value = NormalizeKeyword(term);
            if (value.Length < 2) return;
            if (list.Any(x => string.Equals(x, value, StringComparison.OrdinalIgnoreCase))) return;
            list.Add(value);
        }
        foreach (string term in SplitTerms(Configuration.BlockedKeywords)) Add(term);
        foreach (string term in state.BlockedKeywords) Add(term);
        return list
            .Where(x => !state.AllowedKeywords.Any(a => string.Equals(NormalizeKeyword(a), x, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    public List<string> AllowedForums() => state.AllowedForums.ToList();

    public List<string> AllowedKeywords() => state.AllowedKeywords.ToList();

    static string NormalizeForum(string? term)
    {
        string value = (term ?? "").Trim().TrimStart('#', '＃').Trim();
        if (value.EndsWith("吧", StringComparison.Ordinal) && value.Length > 2)
            value = value[..^1].Trim();
        return value;
    }

    static string NormalizeKeyword(string? term)
        => (term ?? "").Trim().TrimStart('#', '＃').Trim();

    StringComparison BlockComparison()
        => Configuration.BlockCaseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public bool IsForumBlocked(string? forum)
    {
        string value = NormalizeForum(forum);
        if (value.Length < 2) return false;
        return EffectiveBlockedForums().Any(blocked =>
            string.Equals(blocked, value, StringComparison.OrdinalIgnoreCase)
            || value.Contains(blocked, StringComparison.OrdinalIgnoreCase));
    }

    public bool IsTextBlocked(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        foreach (string keyword in EffectiveBlockedKeywords())
        {
            if (text.Contains(keyword, BlockComparison())) return true;
        }
        return false;
    }

    public bool IsItemBlocked(FeedItem item)
    {
        string text = $"{item.Title} {item.Excerpt} {item.Meta}";
        return IsTextBlocked(text);
    }

    // ─────────────────────────────────────────────────────────────
    // 工具：让主人/角色自己写入屏蔽规则
    // ─────────────────────────────────────────────────────────────

    [XmlFunction(FunctionMode.OneShot, "FeedBlock")]
    [Description("把贴吧或关键词加入屏蔽（长期生效）。只在主人明确要求时用，参数只来自主人。")]
    public Task<string> FeedBlock(
        [Description("吧名（如 核战避难所）或关键词（如 nsfw、#性压抑）")] string target,
        [Description("forum/keyword/auto，默认 auto")] string? kind = "auto")
    {
        string raw = (target ?? "").Trim();
        if (raw.Length < 2) return Task.FromResult("屏蔽目标太短。");
        string mode = (kind ?? "auto").Trim().ToLowerInvariant();
        bool forum = mode == "forum" || (mode == "auto" && raw.EndsWith("吧", StringComparison.Ordinal));
        // 明确的 # 开头一律按关键词
        if (raw.StartsWith("#", StringComparison.Ordinal) || raw.StartsWith("＃", StringComparison.Ordinal)) forum = false;

        state.BlockedForums ??= new List<string>();
        state.BlockedKeywords ??= new List<string>();
        state.AllowedForums ??= new List<string>();
        state.AllowedKeywords ??= new List<string>();
        // 想重新屏蔽，先撤销放行（否则配置默认项仍被放行名单挡着）
        state.AllowedForums.RemoveAll(x => string.Equals(NormalizeForum(x), NormalizeForum(raw), StringComparison.OrdinalIgnoreCase));
        state.AllowedKeywords.RemoveAll(x => string.Equals(NormalizeKeyword(x), NormalizeKeyword(raw), StringComparison.OrdinalIgnoreCase));
        if (forum)
        {
            string value = NormalizeForum(raw);
            if (value.Length < 2) return Task.FromResult("吧名太短。");
            if (IsForumBlocked(value)) return Task.FromResult($"「{value}」已经在屏蔽列表里了（放行已撤销）。");
            if (state.BlockedForums.Count >= 200) return Task.FromResult("屏蔽吧名太多（上限 200），先清理一些。");
            state.BlockedForums.Add(value);
            SaveState();
            return Task.FromResult($"已屏蔽贴吧「{value}」，以后不会刷这个吧。当前屏蔽 {EffectiveBlockedForums().Count} 个吧。");
        }
        else
        {
            string value = NormalizeKeyword(raw);
            if (value.Length < 2) return Task.FromResult("关键词太短。");
            if (EffectiveBlockedKeywords().Any(x => string.Equals(x, value, StringComparison.OrdinalIgnoreCase)))
                return Task.FromResult($"关键词「{value}」已经在屏蔽列表里了。");
            if (state.BlockedKeywords.Count >= 200) return Task.FromResult("屏蔽关键词太多（上限 200），先清理一些。");
            state.BlockedKeywords.Add(value);
            SaveState();
            return Task.FromResult($"已屏蔽关键词「{value}」，标题/摘要/正文命中都不再返回。当前屏蔽 {EffectiveBlockedKeywords().Count} 个词。");
        }
    }

    [XmlFunction(FunctionMode.OneShot, "FeedUnblock")]
    [Description("移除一个屏蔽项；配置默认屏蔽会在当前运行放行（只改状态）。只在主人明确要求恢复时用。")]
    public Task<string> FeedUnblock([Description("要恢复的吧名或关键词")] string target)
    {
        string raw = (target ?? "").Trim();
        if (raw.Length < 2) return Task.FromResult("目标太短。");
        state.BlockedForums ??= new List<string>();
        state.BlockedKeywords ??= new List<string>();
        state.AllowedForums ??= new List<string>();
        state.AllowedKeywords ??= new List<string>();
        string forum = NormalizeForum(raw);
        string keyword = NormalizeKeyword(raw);
        bool inConfigForum = SplitTerms(Configuration.BlockedForums)
            .Any(x => string.Equals(NormalizeForum(x), forum, StringComparison.OrdinalIgnoreCase));
        bool inConfigKeyword = SplitTerms(Configuration.BlockedKeywords)
            .Any(x => string.Equals(NormalizeKeyword(x), keyword, StringComparison.OrdinalIgnoreCase));
        bool removedForum = state.BlockedForums.RemoveAll(x => string.Equals(NormalizeForum(x), forum, StringComparison.OrdinalIgnoreCase)) > 0;
        bool removedKeyword = state.BlockedKeywords.RemoveAll(x => string.Equals(NormalizeKeyword(x), keyword, StringComparison.OrdinalIgnoreCase)) > 0;
        if (!removedForum && !removedKeyword && !inConfigForum && !inConfigKeyword)
            return Task.FromResult("屏蔽列表里没有这个目标。");

        string note = "";
        if (inConfigForum)
        {
            if (!state.AllowedForums.Any(x => string.Equals(NormalizeForum(x), forum, StringComparison.OrdinalIgnoreCase)))
                state.AllowedForums.Add(forum);
            note = $"贴吧「{forum}」已放行";
        }
        if (inConfigKeyword)
        {
            if (!state.AllowedKeywords.Any(x => string.Equals(NormalizeKeyword(x), keyword, StringComparison.OrdinalIgnoreCase)))
                state.AllowedKeywords.Add(keyword);
            note = note.Length > 0 ? note + "；关键词「" + keyword + "」也已放行" : $"关键词「{keyword}」已放行";
        }
        SaveState();
        if (note.Length > 0)
            return Task.FromResult($"{note}（只改运行时状态，不改插件配置，发版默认屏蔽保持不变）。");
        return Task.FromResult($"已恢复「{raw}」（角色追加的那份已移除）。");
    }

    [XmlFunction(FunctionMode.OneShot, "FeedBlockList")]
    [Description("查看生效的屏蔽吧/关键词。")]
    public Task<string> FeedBlockList()
    {
        var forums = EffectiveBlockedForums();
        var keywords = EffectiveBlockedKeywords();
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("[贴吧/小黑盒 · 屏蔽列表]");
        sb.AppendLine($"- 屏蔽贴吧（{forums.Count}）：{(forums.Count == 0 ? "无" : string.Join("、", forums))}");
        sb.AppendLine($"- 屏蔽关键词（{keywords.Count}）：{(keywords.Count == 0 ? "无" : string.Join("、", keywords))}");
        if (state.AllowedForums.Count > 0 || state.AllowedKeywords.Count > 0)
            sb.AppendLine($"- 已放行：{string.Join("、", state.AllowedForums.Concat(state.AllowedKeywords))}（只改运行时状态，不改插件配置）");
        sb.AppendLine("- 规则：贴吧命中不刷；标题/摘要/正文命中关键词都不返回。");
        sb.AppendLine("- 主人说“以后别看她刷某个吧/某个标签”时，用 FeedBlock 写入；要恢复用 FeedUnblock（默认屏蔽也能放行，不动配置）。");
        return Task.FromResult(sb.ToString().TrimEnd());
    }
}

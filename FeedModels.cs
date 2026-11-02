using System;
using System.Collections.Generic;

namespace presents.FeedScroll;

public sealed class FeedItem
{
    public string Id { get; set; } = "";
    /// <summary>tieba / xhh</summary>
    public string Site { get; set; } = "tieba";
    public string Title { get; set; } = "";
    public string Url { get; set; } = "";
    public string Author { get; set; } = "";
    public string Meta { get; set; } = "";
    public string Excerpt { get; set; } = "";
    public DateTime SeenAt { get; set; }
}

public static class FeedUrls
{
    /// <summary>把列表里的相对链接补成绝对链接；WebView2.Navigate 不接受 /app/bbs/... 这种相对地址。</summary>
    public static string Normalize(string? url, string? site)
    {
        string value = (url ?? "").Trim();
        if (value.Length == 0) return "";
        if (value.StartsWith("//", StringComparison.Ordinal)) return "https:" + value;
        if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return value;

        string host = (site ?? "").Trim().ToLowerInvariant() == "xhh"
            ? "https://www.xiaoheihe.cn"
            : "https://tieba.baidu.com";
        return value.StartsWith("/", StringComparison.Ordinal) ? host + value : host + "/" + value;
    }
}

/// <summary>角色自己维护的兴趣：它想看什么</summary>
public sealed class FeedInterest
{
    /// <summary>forum = 贴吧吧名；keyword = 搜索关键词</summary>
    public string Kind { get; set; } = "keyword";
    public string Term { get; set; } = "";
    public string Note { get; set; } = "";
    public DateTime AddedAt { get; set; }
    public DateTime LastUsedAt { get; set; }
    public int UseCount { get; set; }
}

public sealed class FeedState
{
    public List<FeedItem> LastItems { get; set; } = new();
    public DateTime LastTiebaAt { get; set; }
    public DateTime LastXhhAt { get; set; }
    public string LastError { get; set; } = "";
    public string FavoriteDayKey { get; set; } = "";
    public int FavoriteCountToday { get; set; }
    public string CommentDayKey { get; set; } = "";
    public int CommentCountToday { get; set; }
    /// <summary>今天真发了几帖（按天重置）</summary>
    public string PostDayKey { get; set; } = "";
    public int PostCountToday { get; set; }
    /// <summary>今天已发过的帖子指纹，防重复</summary>
    public List<string> PostFingerprints { get; set; } = new();
    public DateTime LastActionAt { get; set; }
    /// <summary>今天已用过的评论指纹，避免重复刷屏</summary>
    public List<string> CommentFingerprints { get; set; } = new();
    public DateTime LastImageSaveAt { get; set; }
    public int LastSavedImages { get; set; }
    /// <summary>AI/主人追加的屏蔽吧名（与配置里的合并生效）</summary>
    public List<string> BlockedForums { get; set; } = new();
    /// <summary>AI/主人追加的屏蔽关键词/标签（与配置里的合并生效）</summary>
    public List<string> BlockedKeywords { get; set; } = new();
    /// <summary>放行名单：把插件默认屏蔽里的吧/词临时放行，不改插件配置（发版默认值保持不变）</summary>
    public List<string> AllowedForums { get; set; } = new();
    public List<string> AllowedKeywords { get; set; } = new();
    /// <summary>角色自己的兴趣清单（FeedInterest 写入）</summary>
    public List<FeedInterest> Interests { get; set; } = new();
    /// <summary>自主浏览记录（最近 40 条，「站点:话题」），用来避免连着看同一个</summary>
    public List<string> CuriosityLog { get; set; } = new();
    public string CuriosityDayKey { get; set; } = "";
    public int CuriosityCountToday { get; set; }
    /// <summary>今天已注入几条阅读笔记（按天重置）</summary>
    public string ReadingNoteInjectDayKey { get; set; } = "";
    public int ReadingNoteInjectToday { get; set; }
    /// <summary>定时自主浏览：今日已自动跑了几轮、上次自动跑的时间</summary>
    public string AutoBrowseDayKey { get; set; } = "";
    public int AutoBrowseRoundsToday { get; set; }
    public DateTime LastAutoBrowseAt { get; set; }
    /// <summary>工具文档今日已完整注入了几次</summary>
    public string ToolDocDayKey { get; set; } = "";
    public int ToolDocInjectToday { get; set; }
}

public sealed class FeedListResult
{
    public bool Success { get; set; }
    public bool TimedOut { get; set; }
    public string Url { get; set; } = "";
    public string Error { get; set; } = "";
}

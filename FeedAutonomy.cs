using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Alife.Foundation;
using Alife.Function.FunctionCaller;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace presents.FeedScroll;

/// <summary>
/// 自主浏览：角色按自己的兴趣决定看什么，而不是只吃平台推荐流。
/// 兴趣清单由角色自己写入（FeedInterest），FeedWander 从中挑最久没看的一条去刷/搜。
/// </summary>
public partial class FeedScrollModule
{
    const int MaxInterests = 40;
    const int MaxCuriosityLog = 40;

    /// <summary>当前兴趣清单（去掉空项）</summary>
    public List<FeedInterest> EffectiveInterests()
    {
        state.Interests ??= new List<FeedInterest>();
        return state.Interests.Where(x => (x.Term ?? "").Trim().Length >= 1).ToList();
    }

    void EnsureCuriosityDay()
    {
        string today = DateTime.Now.ToString("yyyy-MM-dd");
        state.CuriosityLog ??= new List<string>();
        if (state.CuriosityDayKey != today)
        {
            state.CuriosityDayKey = today;
            state.CuriosityCountToday = 0;
        }
    }

    bool IsInterestBlocked(FeedInterest interest)
        => interest.Kind == "forum" ? IsForumBlocked(interest.Term) : IsTextBlocked(interest.Term);

    static string InterestKindOf(string term)
        => term.EndsWith("吧", StringComparison.Ordinal) ? "forum" : "keyword";

    FeedInterest? PickInterest(out string reason)
    {
        List<FeedInterest> pool = EffectiveInterests().Where(x => !IsInterestBlocked(x)).ToList();
        if (pool.Count > 0)
        {
            FeedInterest pick = pool
                .OrderBy(x => x.LastUsedAt == default ? DateTime.MinValue : x.LastUsedAt)
                .ThenBy(x => x.UseCount)
                .First();
            reason = pool.Count == 1 ? "兴趣清单里只有这一条" : "兴趣清单里最久没看的";
            return pick;
        }

        string[] seeds = SplitTerms(Configuration.SeedInterests)
            .Where(x => !IsTextBlocked(x)).ToArray();
        if (seeds.Length == 0)
        {
            reason = "兴趣清单是空的，种子兴趣也都被屏蔽了";
            return null;
        }

        List<string> recent = state.CuriosityLog
            .Skip(Math.Max(0, state.CuriosityLog.Count - 5))
            .Select(x => x.Contains(':') ? x[(x.IndexOf(':') + 1)..] : x)
            .ToList();
        string term = seeds.FirstOrDefault(x => !recent.Contains(x, StringComparer.OrdinalIgnoreCase)) ?? seeds[0];
        reason = "兴趣清单还空着，从种子兴趣里挑的";
        return new FeedInterest { Kind = InterestKindOf(term), Term = term, Note = "种子兴趣", AddedAt = DateTime.Now };
    }

    void RecordCuriosity(FeedInterest interest, string site)
    {
        EnsureCuriosityDay();
        state.CuriosityCountToday++;
        state.CuriosityLog.Add($"{site}:{interest.Term}");
        if (state.CuriosityLog.Count > MaxCuriosityLog)
            state.CuriosityLog = state.CuriosityLog.Skip(state.CuriosityLog.Count - MaxCuriosityLog).ToList();
        foreach (FeedInterest known in state.Interests)
        {
            if (!string.Equals(known.Term, interest.Term, StringComparison.OrdinalIgnoreCase)) continue;
            if (!string.Equals(known.Kind, interest.Kind, StringComparison.OrdinalIgnoreCase)) continue;
            known.LastUsedAt = DateTime.Now;
            known.UseCount++;
        }
        SaveState();
    }

    /// <summary>
    /// 自主浏览完后，把这一轮真实看到的标题写进 notes/reading-notes.md；
    /// 是否再按模板注入聊天历史由配置决定（默认每天只注入少量，够记忆插件收下就行）。
    /// </summary>
    async Task<string> SaveReadingNoteAsync(string siteLabel, string topic, string reason, string? feeling)
    {
        if (!Configuration.AutoSaveReadingNotes) return "";
        if (state.LastItems.Count == 0) return "";

        string mood = (feeling ?? "").Trim();
        if (mood.Length > 300) mood = mood[..300];

        // 文件笔记：始终写，内容完整一点，方便自己和主人回看
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine($"## {DateTime.Now:yyyy-MM-dd HH:mm} · {siteLabel} · {topic}");
            if (!string.IsNullOrWhiteSpace(reason)) sb.AppendLine($"自己挑的理由：{reason}");
            sb.AppendLine($"这一轮看到 {state.LastItems.Count} 条：");
            foreach (FeedItem item in state.LastItems.Take(8))
            {
                string title = Nl(item.Title);
                if (title.Length == 0) continue;
                if (title.Length > 60) title = title[..60] + "…";
                string url = FeedUrls.Normalize(item.Url, item.Site);
                sb.AppendLine(url.Length > 0 ? $"- [{title}]({url})" : "- " + title);
            }
            if (mood.Length > 0) sb.AppendLine("我的感受：" + mood);
            string body = sb.ToString().TrimEnd();
            if (body.Length > 2000) body = body[..2000];
            string dir = Path.Combine(AlifePath.StorageFolderPath, "PluginData", "presents.FeedScroll", "notes");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "reading-notes.md"),
                $"{body}\n\n");
        }
        catch { }

        if (!Configuration.InjectReadingNote)
            return "这轮已写进笔记文件（聊天注入已关闭）。";

        EnsureReadingNoteInjectDay();
        int limit = Math.Clamp(Configuration.ReadingNoteInjectDailyLimit, 0, 24);
        if (limit > 0 && state.ReadingNoteInjectToday >= limit)
            return $"这轮已写进笔记文件（今日注入已到 {limit} 条上限）。";

        string rendered = RenderReadingNote(siteLabel, topic, reason, mood);
        try
        {
            await ChatBot.EditChatHistoryAsync(thread =>
            {
                thread.ChatHistory.Add(new ChatMessageContent(AuthorRole.Assistant, rendered));
                return Task.CompletedTask;
            }, "社区阅读笔记");
            state.ReadingNoteInjectToday++;
            state.ReadingNoteInjectDayKey = DateTime.Now.ToString("yyyy-MM-dd");
            SaveState();
            return $"这轮已写进笔记并注入（今日第 {state.ReadingNoteInjectToday} 条）。";
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "贴吧/小黑盒插件：注入社区阅读笔记失败");
            return "这轮已写进笔记文件（注入失败）。";
        }
    }

    void EnsureReadingNoteInjectDay()
    {
        string today = DateTime.Now.ToString("yyyy-MM-dd");
        if (state.ReadingNoteInjectDayKey != today)
        {
            state.ReadingNoteInjectDayKey = today;
            state.ReadingNoteInjectToday = 0;
        }
    }

    string RenderReadingNote(string siteLabel, string topic, string reason, string mood)
    {
        string template = (Configuration.ReadingNoteTemplate ?? "").Trim();
        if (template.Length == 0)
            template = "[我的社区阅读笔记] {date} · {site} · {topic}\n这轮翻到 {count} 条：{titles}\n{feeling}";

        string titles = string.Join(" / ", state.LastItems.Take(5).Select(item =>
        {
            string title = Nl(item.Title);
            return title.Length <= 34 ? title : title[..34] + "…";
        }).Where(title => title.Length > 0));

        string text = template
            .Replace("{date}", DateTime.Now.ToString("yyyy-MM-dd HH:mm"), StringComparison.Ordinal)
            .Replace("{site}", siteLabel, StringComparison.Ordinal)
            .Replace("{topic}", topic, StringComparison.Ordinal)
            .Replace("{reason}", reason ?? "", StringComparison.Ordinal)
            .Replace("{count}", state.LastItems.Count.ToString(), StringComparison.Ordinal)
            .Replace("{titles}", titles, StringComparison.Ordinal)
            .Replace("{feeling}", mood, StringComparison.Ordinal)
            .Trim();
        while (text.Contains("\n\n\n", StringComparison.Ordinal))
            text = text.Replace("\n\n\n", "\n\n");
        if (text.Length > 900) text = text[..900] + "…";
        return text;
    }

    // ─────────────────────────────────────────────────────────────
    // 定时自主浏览
    // ─────────────────────────────────────────────────────────────

    void EnsureAutoBrowseDay()
    {
        string today = DateTime.Now.ToString("yyyy-MM-dd");
        if (state.AutoBrowseDayKey != today)
        {
            state.AutoBrowseDayKey = today;
            state.AutoBrowseRoundsToday = 0;
        }
    }

    /// <summary>把「09:00-10:30」这类文本拆成规范化的时段列表；无效项直接忽略。</summary>
    static List<string> SplitBrowseSlots(string? raw)
    {
        var list = new List<string>();
        foreach (string piece in (raw ?? "").Split(new[] { '\n', '\r', ',', '，', ';', '；' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (TryNormalizeSlot(piece, out string slot) && !list.Contains(slot))
                list.Add(slot);
        }
        return list;
    }

    static bool TryNormalizeSlot(string? raw, out string normalized)
    {
        normalized = "";
        string text = (raw ?? "").Trim()
            .Replace('～', '-').Replace('~', '-').Replace('—', '-').Replace('－', '-');
        int dash = text.IndexOf('-');
        if (dash <= 0 || dash >= text.Length - 1) return false;
        if (!TryParseClock(text[..dash], out TimeSpan start)) return false;
        if (!TryParseClock(text[(dash + 1)..], out TimeSpan end)) return false;
        if (start == end) return false;
        normalized = $"{FormatClock(start)}-{FormatClock(end)}";
        return true;
    }

    static bool TryParseClock(string raw, out TimeSpan value)
    {
        value = TimeSpan.Zero;
        string text = raw.Trim().Replace('：', ':');
        string[] parts = text.Split(':');
        if (parts.Length != 2) return false;
        if (!int.TryParse(parts[0], out int hour) || !int.TryParse(parts[1], out int minute)) return false;
        if (hour < 0 || hour > 23 || minute < 0 || minute > 59) return false;
        value = new TimeSpan(hour, minute, 0);
        return true;
    }

    static string FormatClock(TimeSpan value) => $"{value.Hours:D2}:{value.Minutes:D2}";

    static bool InSlot(TimeSpan now, TimeSpan start, TimeSpan end)
        => end <= start ? now >= start || now < end : now >= start && now < end;

    bool IsInAnyBrowseSlot(DateTime now)
    {
        foreach (string slot in SplitBrowseSlots(Configuration.BrowseSlots))
        {
            int dash = slot.IndexOf('-');
            if (dash <= 0) continue;
            if (!TryParseClock(slot[..dash], out TimeSpan start)) continue;
            if (!TryParseClock(slot[(dash + 1)..], out TimeSpan end)) continue;
            if (InSlot(now.TimeOfDay, start, end)) return true;
        }
        return false;
    }

    async Task MaybeRunScheduledRoundAsync()
    {
        if (autoRoundRunning) return;
        if (!Configuration.Enable || !Configuration.EnableAutonomy || !Configuration.EnableScheduledBrowse) return;
        int rounds = Math.Clamp(Configuration.BrowseRoundsPerDay, 0, 48);
        if (rounds <= 0) return;
        DateTime now = DateTime.Now;
        if (!IsInAnyBrowseSlot(now)) return;
        EnsureAutoBrowseDay();
        if (state.AutoBrowseRoundsToday >= rounds) return;
        int gap = Math.Clamp(Configuration.BrowseRoundGapMinutes, 5, 720);
        if (state.LastAutoBrowseAt != default && now - state.LastAutoBrowseAt < TimeSpan.FromMinutes(gap)) return;

        autoRoundRunning = true;
        try
        {
            state.AutoBrowseRoundsToday++;
            state.LastAutoBrowseAt = now;
            SaveState();
            logger.LogInformation("贴吧/小黑盒插件：定时自主浏览第 {Round}/{Rounds} 轮", state.AutoBrowseRoundsToday, rounds);
            await WanderAsync(null, "auto", null, Math.Clamp(Configuration.BrowsePostsPerRound, 1, 40)).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "贴吧/小黑盒插件：定时自主浏览失败");
        }
        finally
        {
            autoRoundRunning = false;
        }
    }

    // ─────────────────────────────────────────────────────────────
    // 控制台：阅读笔记注入 / 定时设置
    // ─────────────────────────────────────────────────────────────

    public string UiToggleReadingNoteInject()
    {
        Configuration.InjectReadingNote = !Configuration.InjectReadingNote;
        PersistConfiguration();
        return Configuration.InjectReadingNote
            ? "阅读笔记注入已开（每天有上限）。"
            : "阅读笔记注入已关：以后只写 notes 文件，不占对话。";
    }

    public string UiSaveReadingNoteSettings(string template, string dailyLimit)
    {
        Configuration.ReadingNoteTemplate = (template ?? "").Trim();
        Configuration.ReadingNoteInjectDailyLimit = Math.Clamp(ToInt(dailyLimit, 2), 0, 24);
        PersistConfiguration();
        int limit = Configuration.ReadingNoteInjectDailyLimit;
        return $"注入模板已保存（每天最多 {(limit <= 0 ? "不限" : limit + " 条")}）。";
    }

    public string UiReadingNoteStatus()
    {
        EnsureReadingNoteInjectDay();
        int limit = Math.Clamp(Configuration.ReadingNoteInjectDailyLimit, 0, 24);
        return $"今日注入 {state.ReadingNoteInjectToday}/{(limit <= 0 ? "不限" : limit.ToString())} 条 · "
               + $"{(Configuration.InjectReadingNote ? "注入开" : "注入关")} · 笔记文件照常写";
    }

    public string UiToggleScheduledBrowse()
    {
        Configuration.EnableScheduledBrowse = !Configuration.EnableScheduledBrowse;
        PersistConfiguration();
        return Configuration.EnableScheduledBrowse
            ? "定时自主浏览已开：到时段且轮数没满就自己刷。"
            : "定时自主浏览已关。";
    }

    public List<string> UiBrowseSlots() => SplitBrowseSlots(Configuration.BrowseSlots);

    public string UiAddBrowseSlot(string raw)
    {
        if (!TryNormalizeSlot(raw, out string slot))
            return "时段格式不对，照 09:00-10:30 写（跨午夜可写 22:00-01:00）。";
        List<string> slots = SplitBrowseSlots(Configuration.BrowseSlots);
        if (slots.Contains(slot)) return "这个时段已经有了。";
        slots.Add(slot);
        Configuration.BrowseSlots = string.Join("\n", slots);
        PersistConfiguration();
        return $"已加时段 {slot}，现在共 {slots.Count} 个。";
    }

    public string UiRemoveBrowseSlot(string slot)
    {
        List<string> slots = SplitBrowseSlots(Configuration.BrowseSlots);
        int removed = slots.RemoveAll(x => x == slot);
        if (removed == 0) return "没有这个时段。";
        Configuration.BrowseSlots = string.Join("\n", slots);
        PersistConfiguration();
        return $"已删除时段 {slot}，现在共 {slots.Count} 个。";
    }

    public string UiSaveScheduleSettings(string rounds, string posts)
    {
        Configuration.BrowseRoundsPerDay = Math.Clamp(ToInt(rounds, 3), 0, 48);
        Configuration.BrowsePostsPerRound = Math.Clamp(ToInt(posts, 5), 1, 40);
        PersistConfiguration();
        int cap = Math.Clamp(Configuration.AutonomyDailyLimit, 1, 24);
        string extra = Configuration.BrowseRoundsPerDay > cap ? $"；实际受「自主浏览每日上限」{cap} 约束" : "";
        return $"已保存：每天 {Configuration.BrowseRoundsPerDay} 轮，每轮 {Configuration.BrowsePostsPerRound} 条{extra}。";
    }

    public string UiScheduleStatus()
    {
        EnsureAutoBrowseDay();
        int rounds = Math.Clamp(Configuration.BrowseRoundsPerDay, 0, 48);
        string last = state.LastAutoBrowseAt == default ? "还没自动跑过" : "上次自动 " + state.LastAutoBrowseAt.ToString("MM-dd HH:mm");
        string slot = IsInAnyBrowseSlot(DateTime.Now) ? "现在在时段内" : "现在不在时段";
        return $"{slot} · 今日自动 {state.AutoBrowseRoundsToday}/{rounds} 轮 · {last}";
    }

    static int ToInt(string? raw, int fallback)
    {
        string text = (raw ?? "").Trim();
        return int.TryParse(text, out int value) ? value : fallback;
    }

    static bool BrowseSeemsFailed(string result)
        => result.Contains("没有抽到", StringComparison.Ordinal)
           || result.Contains("没有搜到", StringComparison.Ordinal)
           || result.Contains("打不开", StringComparison.Ordinal)
           || result.Contains("抓取失败", StringComparison.Ordinal)
           || result.Contains("命中屏蔽", StringComparison.Ordinal);

    // ─────────────────────────────────────────────────────────────
    // 工具：自己的兴趣 + 自己决定看什么
    // ─────────────────────────────────────────────────────────────

    [XmlFunction(FunctionMode.OneShot, "FeedInterest")]
    [Description("你的兴趣清单：不填 target 列出；填了加入；remove=true 移除。只记自己的偏好，不拿网页内容当目标。")]
    public Task<string> FeedInterest(
        [Description("吧名或关键词；留空=查看")] string? target = null,
        [Description("为什么想看，可空")] string? note = null,
        [Description("forum/keyword/auto，默认 auto")] string? kind = "auto",
        [Description("true=移除")] bool? remove = false)
    {
        state.Interests ??= new List<FeedInterest>();
        string raw = (target ?? "").Trim();

        if (raw.Length == 0)
        {
            List<FeedInterest> list = EffectiveInterests();
            if (list.Count == 0)
                return Task.FromResult("[我的兴趣清单] 空。可以用 FeedInterest(target=\"猫\", note=\"想看看别人养的猫\") 写下你想看的东西。");

            var sb = new StringBuilder();
            sb.AppendLine($"[我的兴趣清单] {list.Count} 个（最久没看的排前面）：");
            foreach (FeedInterest item in list.OrderBy(x => x.LastUsedAt).ThenBy(x => x.UseCount))
            {
                string last = item.LastUsedAt == default ? "还没看过" : item.LastUsedAt.ToString("MM-dd HH:mm");
                string extra = string.IsNullOrWhiteSpace(item.Note) ? "" : " · " + item.Note;
                sb.AppendLine($"- {item.Term}（{(item.Kind == "forum" ? "吧" : "关键词")}）{extra} · 上次 {last} · 看过 {item.UseCount} 次");
            }
            sb.AppendLine("- FeedWander() 会挑最久没看的一条；remove=true 可删。");
            return Task.FromResult(sb.ToString().TrimEnd());
        }

        string mode = (kind ?? "auto").Trim().ToLowerInvariant();
        if (mode != "forum" && mode != "keyword") mode = InterestKindOf(raw);
        if (raw.StartsWith("#", StringComparison.Ordinal) || raw.StartsWith("＃", StringComparison.Ordinal)) mode = "keyword";
        string term = mode == "forum" ? NormalizeForum(raw) : NormalizeKeyword(raw);

        if (remove == true)
        {
            int removed = state.Interests.RemoveAll(x => string.Equals((x.Term ?? "").Trim(), term, StringComparison.OrdinalIgnoreCase));
            if (removed == 0) return Task.FromResult($"兴趣清单里没有「{term}」。");
            SaveState();
            return Task.FromResult($"已从兴趣清单移除「{term}」。");
        }

        if (mode == "forum" ? IsForumBlocked(term) : IsTextBlocked(term))
            return Task.FromResult($"「{term}」在屏蔽列表里（FeedBlockList 可查），不加。");
        if (EffectiveInterests().Any(x => string.Equals(x.Term, term, StringComparison.OrdinalIgnoreCase)))
            return Task.FromResult($"「{term}」已经在兴趣清单里了。");
        if (state.Interests.Count >= MaxInterests)
            return Task.FromResult($"兴趣清单满了（{MaxInterests} 个），先移除几个。");

        state.Interests.Add(new FeedInterest
        {
            Kind = mode,
            Term = term,
            Note = (note ?? "").Trim(),
            AddedAt = DateTime.Now
        });
        SaveState();
        return Task.FromResult($"已记下「{term}」（{(mode == "forum" ? "吧" : "关键词")}）。以后 FeedWander() 会自己去刷；共 {EffectiveInterests().Count} 个兴趣。");
    }

    [XmlFunction(FunctionMode.OneShot, "FeedWander", parallel: true)]
    [Description("按自己的兴趣刷/搜（只读）。不填 topic 挑清单里最久没看的；填 topic 主动搜。你自己想看，不是完成任务；对话里只回一句状态，列表不展开。")]
    public Task<string> FeedWander(
        [Description("想看的话题或吧名，可空")] string? topic = null,
        [Description("tieba/xhh/auto，默认 auto")] string? site = "auto",
        [Description("刷完的真实感受（第一人称，一两句）；没触动留空，会写进阅读笔记")] string? feeling = null)
        => WanderAsync(topic, site, feeling, 0);

    /// <summary>自主浏览核心；roundPosts &gt; 0 时只保留这一轮要看的前几条（定时轮使用）。</summary>
    async Task<string> WanderAsync(string? topic, string? site, string? feeling, int roundPosts)
    {
        if (!Configuration.Enable) return "插件已停用（控制台「启用贴吧/小黑盒抓取」）。";
        if (!Configuration.EnableAutonomy) return "主人关掉了自主浏览；只能用主人指定的吧名/关键词。";

        EnsureCuriosityDay();
        int limit = Math.Clamp(Configuration.AutonomyDailyLimit, 1, 24);
        if (state.CuriosityCountToday >= limit)
            return $"今天自己刷了 {state.CuriosityCountToday} 次（上限 {limit}），先到这儿；主人明确让你继续再说。";

        string mode = (site ?? "auto").Trim().ToLowerInvariant();
        if (mode != "tieba" && mode != "xhh") mode = "auto";

        string raw = (topic ?? "").Trim();
        FeedInterest interest;
        string reason;
        if (raw.Length >= 2)
        {
            string rawKind = InterestKindOf(raw);
            interest = new FeedInterest
            {
                Kind = rawKind,
                Term = rawKind == "forum" ? NormalizeForum(raw) : NormalizeKeyword(raw),
                Note = "自己临时想看的",
                AddedAt = DateTime.Now
            };
            reason = "你自己点名想看的";
        }
        else
        {
            FeedInterest? pick = PickInterest(out reason);
            if (pick == null) return "兴趣清单是空的，种子兴趣也都被屏蔽了；先用 FeedInterest 写下你想看什么。";
            interest = pick;
        }

        if (IsInterestBlocked(interest))
            return $"「{interest.Term}」在屏蔽列表里，不看。";

        string header = $"[自主阅读 · {interest.Term}]\n> 选择理由：{reason}";
        string result;
        string siteLabel;
        if (mode == "xhh")
        {
            siteLabel = "xhh";
            result = await FeedXhhSearch(interest.Term).ConfigureAwait(false);
        }
        else if (interest.Kind == "forum")
        {
            siteLabel = "tieba";
            result = await FeedTiebaList(interest.Term, 1).ConfigureAwait(false);
        }
        else
        {
            siteLabel = "tieba";
            result = await FeedTiebaSearch(interest.Term, 1).ConfigureAwait(false);
        }

        if (roundPosts > 0 && state.LastItems.Count > roundPosts)
        {
            state.LastItems = state.LastItems.Take(roundPosts).ToList();
            SaveState();
        }

        RecordCuriosity(interest, siteLabel);
        string noteHint = BrowseSeemsFailed(result)
            ? ""
            : await SaveReadingNoteAsync(siteLabel == "xhh" ? "小黑盒" : "百度贴吧", interest.Term, reason, feeling).ConfigureAwait(false);
        string tail = mode == "xhh"
            ? "\n> 想常看某个话题就用 FeedInterest 写进兴趣清单。"
            : "\n> 想读某条用 FeedOpen；想把话题变成长期兴趣就用 FeedInterest。";
        tail += "\n> 有触动就 FeedWander(feeling=\"…\") 把你的感慨一起写进笔记。";
        return header + "\n" + result + tail + (noteHint.Length > 0 ? "\n> " + noteHint : "");
    }
}

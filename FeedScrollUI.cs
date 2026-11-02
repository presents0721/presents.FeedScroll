using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Alife.Framework;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace presents.FeedScroll;

public class FeedScrollUI : ModuleUIBase<FeedScrollModule, FeedScrollConfig>
{
    string keyword = "原神";
    string message = "";
    bool busy;
    string blockForumInput = "";
    string blockKeywordInput = "";
    string unblockInput = "";
    string interestInput = "";
    string slotInput = "";
    string noteTemplateDraft = "";
    string injectLimitDraft = "";
    string roundsDraft = "";
    string postsDraft = "";
    string toolDescDraft = "";
    string toolExpDraft = "";
    string toolLimitDraft = "";
    bool draftsLoaded;

    void EnsureDrafts()
    {
        if (draftsLoaded || Module == null) return;
        draftsLoaded = true;
        noteTemplateDraft = Module.Configuration.ReadingNoteTemplate ?? "";
        injectLimitDraft = Module.Configuration.ReadingNoteInjectDailyLimit.ToString();
        roundsDraft = Module.Configuration.BrowseRoundsPerDay.ToString();
        postsDraft = Module.Configuration.BrowsePostsPerRound.ToString();
        toolDescDraft = Module.Configuration.ToolPromptDescription ?? "";
        toolExpDraft = Module.Configuration.ToolPromptExplanation ?? "";
        toolLimitDraft = Module.Configuration.ToolDocInjectDailyLimit.ToString();
    }

    protected override void BuildRenderTree(RenderTreeBuilder b)
    {
        base.BuildRenderTree(b);
        EnsureDrafts();
        int seq = 0;
        b.AddMarkupContent(seq++, "<style>" + Css + "</style>");

        b.OpenElement(seq++, "div");
        b.AddAttribute(seq++, "class", "fds-root");
        AddHtml(b, ref seq,
            $"<div class=\"fds-header\"><div><div class=\"fds-title\">刷贴吧与小黑盒</div>"
            + $"<div class=\"fds-muted\">只读 · WebView2 · 防注入</div></div>"
            + $"<div class=\"fds-badges\"><span class=\"fds-badge fds-badge-list\">列表 {Module?.UiListCount ?? 0} 条</span>"
            + $"<span class=\"fds-badge {(Module?.Configuration.Enable == true ? "fds-badge-on" : "fds-badge-off")}\">{(Module?.Configuration.Enable == true ? "抓取已启用" : "抓取已停用")}</span></div></div>");

        if (!string.IsNullOrWhiteSpace(message))
            b.AddMarkupContent(seq++, $"<div class=\"fds-msg\">{E(message)}</div>");

        b.OpenElement(seq++, "div");
        b.AddAttribute(seq++, "class", "fds-grid");

        b.OpenElement(seq++, "div");
        b.AddAttribute(seq++, "class", "fds-card fds-card-status");
        b.AddMarkupContent(seq++, "<div class=\"fds-card-title\">浏览器状态</div>");
        b.OpenElement(seq++, "details");
        b.AddAttribute(seq++, "class", "fds-details");
        b.OpenElement(seq++, "summary");
        b.AddContent(seq++, "运行详情");
        b.CloseElement();
        b.OpenElement(seq++, "pre");
        b.AddAttribute(seq++, "class", "fds-pre");
        b.AddContent(seq++, Module?.UiStatusText() ?? "模块未激活");
        b.CloseElement();
        b.CloseElement();
        b.OpenElement(seq++, "div");
        b.AddAttribute(seq++, "class", "fds-row");
        Button(b, ref seq, "打开浏览器窗口（登录/验证）", async () =>
        {
            busy = true;
            message = "正在启动浏览器…";
            StateHasChanged();
            message = await Module!.OpenBrowserWindow("tieba");
            busy = false;
            StateHasChanged();
        }, busy);
        Button(b, ref seq, "打开小黑盒（登录）", async () =>
        {
            busy = true;
            message = "正在启动浏览器并进入小黑盒…";
            StateHasChanged();
            message = await Module!.OpenBrowserWindow("xhh");
            busy = false;
            StateHasChanged();
        }, busy);
        Button(b, ref seq, "刷新状态", () =>
        {
            message = "";
            StateHasChanged();
        }, busy);
        b.CloseElement();
        b.CloseElement();

        b.OpenElement(seq++, "div");
        b.AddAttribute(seq++, "class", "fds-card fds-card-try");
        b.AddMarkupContent(seq++, "<div class=\"fds-card-title\">试刷</div>");
        b.OpenElement(seq++, "div");
        b.AddAttribute(seq++, "class", "fds-row");
        b.OpenElement(seq++, "input");
        b.AddAttribute(seq++, "class", "fds-input");
        b.AddAttribute(seq++, "value", keyword);
        b.AddAttribute(seq++, "placeholder", "贴吧吧名 / 小黑盒关键词");
        b.AddAttribute(seq++, "oninput", EventCallback.Factory.Create<ChangeEventArgs>(this, e =>
        {
            keyword = e.Value?.ToString() ?? "";
        }));
        b.CloseElement();
        Button(b, ref seq, "刷贴吧列表", async () =>
        {
            if (string.IsNullOrWhiteSpace(keyword)) return;
            busy = true;
            message = "抓取贴吧列表…";
            StateHasChanged();
            await Module!.FeedTiebaList(keyword, 1);
            message = $"贴吧列表已抓取：{Module.UiListCount} 条（标题在面板日志，正文不注入对话）";
            busy = false;
            StateHasChanged();
        }, busy);
        Button(b, ref seq, "搜贴吧", async () =>
        {
            if (string.IsNullOrWhiteSpace(keyword)) return;
            busy = true;
            message = "搜索贴吧…";
            StateHasChanged();
            await Module!.FeedTiebaSearch(keyword, 1);
            message = $"贴吧搜索已抓取：{Module.UiListCount} 条";
            busy = false;
            StateHasChanged();
        }, busy);
        Button(b, ref seq, "刷小黑盒", async () =>
        {
            busy = true;
            message = "抓取小黑盒社区…";
            StateHasChanged();
            await Module!.FeedXhhList();
            message = $"小黑盒列表已抓取：{Module.UiListCount} 条";
            busy = false;
            StateHasChanged();
        }, busy);
        Button(b, ref seq, "搜小黑盒", async () =>
        {
            if (string.IsNullOrWhiteSpace(keyword)) return;
            busy = true;
            message = "搜索小黑盒…";
            StateHasChanged();
            await Module!.FeedXhhSearch(keyword);
            message = $"小黑盒搜索已抓取：{Module.UiListCount} 条";
            busy = false;
            StateHasChanged();
        }, busy);
        Button(b, ref seq, "按 AI 兴趣刷一条", async () =>
        {
            busy = true;
            message = "按角色自己的兴趣清单挑一条…";
            StateHasChanged();
            string result = await Module!.FeedWander(null, "auto");
            string[] lines = result.Split('\n');
            message = lines.Length > 1 ? $"{lines[0]} {lines[1]}（列表 {Module.UiListCount} 条，详情在面板日志）" : result;
            busy = false;
            StateHasChanged();
        }, busy);
        b.CloseElement();
        b.CloseElement();

        // 屏蔽规则
        List<string> blockedForums = Module?.EffectiveBlockedForums() ?? new List<string>();
        List<string> blockedKeywords = Module?.EffectiveBlockedKeywords() ?? new List<string>();
        b.OpenElement(seq++, "div");
        b.AddAttribute(seq++, "class", "fds-card fds-card-block");
        AddHtml(b, ref seq, "<div class=\"fds-card-title\">屏蔽规则 <span class=\"fds-muted\">吧不刷 · 标题/摘要/正文命中关键词都不返回</span></div>");
        AddHtml(b, ref seq, $"<div class=\"fds-sub\">屏蔽贴吧 · {blockedForums.Count}</div>");
        AddHtml(b, ref seq, blockedForums.Count == 0
            ? "<div class=\"fds-muted\">还没有额外屏蔽的吧。</div>"
            : "<div class=\"fds-chips\">" + string.Concat(blockedForums.Select(x => $"<span class=\"fds-chip\">{E(x)}</span>")) + "</div>");
        AddHtml(b, ref seq, $"<div class=\"fds-sub\">屏蔽关键词 · {blockedKeywords.Count}</div>");
        AddHtml(b, ref seq, blockedKeywords.Count == 0
            ? "<div class=\"fds-muted\">还没有关键词级屏蔽。</div>"
            : "<div class=\"fds-chips\">" + string.Concat(blockedKeywords.Select(x => $"<span class=\"fds-chip\">{E(x)}</span>")) + "</div>");
        var allowedForums = Module?.UiAllowedForums() ?? new List<string>();
        var allowedKeywords = Module?.UiAllowedKeywords() ?? new List<string>();
        if (allowedForums.Count > 0 || allowedKeywords.Count > 0)
        {
            AddHtml(b, ref seq, $"<div class=\"fds-sub\">已放行 · {allowedForums.Count + allowedKeywords.Count} <span class=\"fds-muted\">只改运行时，插件默认值不动</span></div>");
            AddHtml(b, ref seq, "<div class=\"fds-chips\">"
                + string.Concat(allowedForums.Concat(allowedKeywords).Select(x => $"<span class=\"fds-chip\">{E(x)}</span>"))
                + "</div>");
        }
        b.OpenElement(seq++, "div");
        b.AddAttribute(seq++, "class", "fds-row");
        TextInput(b, ref seq, blockForumInput, "要屏蔽的吧名，如 核战避难所", v => blockForumInput = v);
        Button(b, ref seq, "屏蔽这个吧", () =>
        {
            message = Module?.UiBlockTarget(blockForumInput, true) ?? "模块未激活";
            blockForumInput = "";
            StateHasChanged();
        }, busy);
        b.CloseElement();
        b.OpenElement(seq++, "div");
        b.AddAttribute(seq++, "class", "fds-row");
        TextInput(b, ref seq, blockKeywordInput, "要屏蔽的关键词/标签，如 #性压抑", v => blockKeywordInput = v);
        Button(b, ref seq, "屏蔽关键词", () =>
        {
            message = Module?.UiBlockTarget(blockKeywordInput, false) ?? "模块未激活";
            blockKeywordInput = "";
            StateHasChanged();
        }, busy);
        b.CloseElement();
        b.OpenElement(seq++, "div");
        b.AddAttribute(seq++, "class", "fds-row");
        TextInput(b, ref seq, unblockInput, "要恢复的吧名或关键词", v => unblockInput = v);
        Button(b, ref seq, "恢复", () =>
        {
            message = Module?.UiUnblock(unblockInput) ?? "模块未激活";
            unblockInput = "";
            StateHasChanged();
        }, busy);
        b.CloseElement();
        AddHtml(b, ref seq, "<div class=\"fds-muted\">配置里的默认屏蔽也能在这里放行：只写运行时状态，不改插件配置、不影响发版默认值。</div>");
        b.CloseElement();

        // 兴趣清单
        List<FeedInterest> interests = Module?.UiInterests() ?? new List<FeedInterest>();
        b.OpenElement(seq++, "div");
        b.AddAttribute(seq++, "class", "fds-card fds-card-interest");
        AddHtml(b, ref seq, $"<div class=\"fds-card-title\">角色自己的兴趣清单 <span class=\"fds-muted\">{interests.Count} 个 · 最久没看的排前面</span></div>");
        if (interests.Count == 0)
            AddHtml(b, ref seq, "<div class=\"fds-muted\">还是空的。可以在这里替它加，也可以让它自己用 FeedInterest 写。</div>");
        foreach (FeedInterest interest in interests.OrderBy(x => x.LastUsedAt).ThenBy(x => x.UseCount))
        {
            string last = interest.LastUsedAt == default ? "还没看过" : interest.LastUsedAt.ToString("MM-dd HH:mm");
            string note = string.IsNullOrWhiteSpace(interest.Note) ? "" : " · " + interest.Note;
            b.OpenElement(seq++, "div");
            b.AddAttribute(seq++, "class", "fds-item");
            AddHtml(b, ref seq, $"<span><b>{E(interest.Term)}</b> <span class=\"fds-muted\">{(interest.Kind == "forum" ? "吧" : "关键词")}{E(note)} · 上次 {E(last)} · 看过 {interest.UseCount} 次</span></span>");
            Button(b, ref seq, "移除", () =>
            {
                message = Module?.UiRemoveInterest(interest.Term) ?? "模块未激活";
                StateHasChanged();
            }, busy);
            b.CloseElement();
        }
        b.OpenElement(seq++, "div");
        b.AddAttribute(seq++, "class", "fds-row");
        TextInput(b, ref seq, interestInput, "想让它以后常看的话题/吧名", v => interestInput = v);
        Button(b, ref seq, "加为吧", () =>
        {
            message = Module?.UiAddInterest(interestInput, true) ?? "模块未激活";
            interestInput = "";
            StateHasChanged();
        }, busy);
        Button(b, ref seq, "加为关键词", () =>
        {
            message = Module?.UiAddInterest(interestInput, false) ?? "模块未激活";
            interestInput = "";
            StateHasChanged();
        }, busy);
        b.CloseElement();
        b.CloseElement();

        // 自主阅读：注入模板 + 定时时段
        b.OpenElement(seq++, "div");
        b.AddAttribute(seq++, "class", "fds-card fds-card-auto");
        AddHtml(b, ref seq, "<div class=\"fds-card-title\">自主阅读 · 注入与定时 <span class=\"fds-muted\">笔记照常落盘；注入可改模板、可关、可限次</span></div>");
        b.OpenElement(seq++, "div");
        b.AddAttribute(seq++, "class", "fds-auto-split");

        // 左：阅读笔记注入
        b.OpenElement(seq++, "div");
        AddHtml(b, ref seq, "<div class=\"fds-sub\">阅读笔记注入</div>");
        bool injectOn = Module?.Configuration.InjectReadingNote ?? true;
        b.OpenElement(seq++, "div");
        b.AddAttribute(seq++, "class", "fds-row");
        Button(b, ref seq, injectOn ? "注入已开（点击关闭）" : "注入已关（点击开启）", () =>
        {
            message = Module?.UiToggleReadingNoteInject() ?? "模块未激活";
            StateHasChanged();
        }, busy);
        AddHtml(b, ref seq, $"<span class=\"fds-muted\">{E(Module?.UiReadingNoteStatus() ?? "")}</span>");
        b.CloseElement();
        TextArea(b, ref seq, noteTemplateDraft,
            "注入模板：{date} {site} {topic} {reason} {count} {titles} {feeling}", 4,
            v => noteTemplateDraft = v);
        AddHtml(b, ref seq, "<div class=\"fds-muted\">占位符：{date} {site} {topic} {reason} {count} {titles} {feeling}。删掉 {feeling} 就只把感受写进文件。</div>");
        b.OpenElement(seq++, "div");
        b.AddAttribute(seq++, "class", "fds-row");
        AddHtml(b, ref seq, "<span class=\"fds-muted\">每天最多注入</span>");
        NumberInput(b, ref seq, injectLimitDraft, "0", "24", "2", v => injectLimitDraft = v);
        AddHtml(b, ref seq, "<span class=\"fds-muted\">条（0 = 不限）</span>");
        Button(b, ref seq, "保存注入设置", () =>
        {
            message = Module?.UiSaveReadingNoteSettings(noteTemplateDraft, injectLimitDraft) ?? "模块未激活";
            StateHasChanged();
        }, busy);
        b.CloseElement();
        b.CloseElement();

        // 右：定时自主浏览
        b.OpenElement(seq++, "div");
        AddHtml(b, ref seq, "<div class=\"fds-sub\">定时自主浏览</div>");
        bool scheduleOn = Module?.Configuration.EnableScheduledBrowse ?? false;
        b.OpenElement(seq++, "div");
        b.AddAttribute(seq++, "class", "fds-row");
        Button(b, ref seq, scheduleOn ? "定时已开（点击关闭）" : "定时已关（点击开启）", () =>
        {
            message = Module?.UiToggleScheduledBrowse() ?? "模块未激活";
            StateHasChanged();
        }, busy);
        AddHtml(b, ref seq, $"<span class=\"fds-muted\">{E(Module?.UiScheduleStatus() ?? "")}</span>");
        b.CloseElement();

        List<string> slots = Module?.UiBrowseSlots() ?? new List<string>();
        if (slots.Count == 0)
        {
            AddHtml(b, ref seq, "<div class=\"fds-muted\">还没有时段；下面的输入框加一个，比如 08:30-09:10。</div>");
        }
        else
        {
            b.OpenElement(seq++, "div");
            b.AddAttribute(seq++, "class", "fds-chips");
            foreach (string slot in slots)
            {
                b.OpenElement(seq++, "span");
                b.AddAttribute(seq++, "class", "fds-chip fds-chip-slot");
                b.AddContent(seq++, slot);
                b.OpenElement(seq++, "button");
                b.AddAttribute(seq++, "type", "button");
                b.AddAttribute(seq++, "class", "fds-chip-x");
                b.AddAttribute(seq++, "title", "删除这个时段");
                b.AddAttribute(seq++, "onclick", EventCallback.Factory.Create<MouseEventArgs>(this, () =>
                {
                    message = Module?.UiRemoveBrowseSlot(slot) ?? "模块未激活";
                    StateHasChanged();
                }));
                b.AddContent(seq++, "×");
                b.CloseElement();
                b.CloseElement();
            }
            b.CloseElement();
        }

        b.OpenElement(seq++, "div");
        b.AddAttribute(seq++, "class", "fds-row");
        TextInput(b, ref seq, slotInput, "新增时段，如 08:30-09:10", v => slotInput = v);
        Button(b, ref seq, "加时段", () =>
        {
            message = Module?.UiAddBrowseSlot(slotInput) ?? "模块未激活";
            slotInput = "";
            StateHasChanged();
        }, busy);
        b.CloseElement();
        b.OpenElement(seq++, "div");
        b.AddAttribute(seq++, "class", "fds-row");
        AddHtml(b, ref seq, "<span class=\"fds-muted\">每天</span>");
        NumberInput(b, ref seq, roundsDraft, "0", "48", "3", v => roundsDraft = v);
        AddHtml(b, ref seq, "<span class=\"fds-muted\">轮，每轮</span>");
        NumberInput(b, ref seq, postsDraft, "1", "40", "5", v => postsDraft = v);
        AddHtml(b, ref seq, "<span class=\"fds-muted\">条</span>");
        Button(b, ref seq, "保存定时设置", () =>
        {
            message = Module?.UiSaveScheduleSettings(roundsDraft, postsDraft) ?? "模块未激活";
            StateHasChanged();
        }, busy);
        b.CloseElement();
        AddHtml(b, ref seq, "<div class=\"fds-muted\">轮数为 0 = 不自动刷；两轮之间默认隔 60 分钟（可在上方配置区改）。</div>");
        b.CloseElement();

        b.CloseElement();
        b.CloseElement();

        // 工具文档注入
        b.OpenElement(seq++, "div");
        b.AddAttribute(seq++, "class", "fds-card fds-card-tooldoc");
        AddHtml(b, ref seq, "<div class=\"fds-card-title\">工具文档注入 <span class=\"fds-muted\">AI 调 &lt;FeedScrollModule/&gt; 时展开的文档；可限次、可关、可直接改文案</span></div>");
        bool toolDocOn = Module?.Configuration.InjectToolDocument ?? true;
        b.OpenElement(seq++, "div");
        b.AddAttribute(seq++, "class", "fds-row");
        Button(b, ref seq, toolDocOn ? "注入已开（点击关闭）" : "注入已关（点击开启）", () =>
        {
            message = Module?.UiToggleToolDocument() ?? "模块未激活";
            StateHasChanged();
        }, busy);
        AddHtml(b, ref seq, $"<span class=\"fds-muted\">{E(Module?.UiToolDocStatus() ?? "")}</span>");
        b.CloseElement();

        b.OpenElement(seq++, "div");
        b.AddAttribute(seq++, "class", "fds-row");
        AddHtml(b, ref seq, "<span class=\"fds-muted\">每天最多完整注入</span>");
        NumberInput(b, ref seq, toolLimitDraft, "0", "24", "2", v => toolLimitDraft = v);
        AddHtml(b, ref seq, "<span class=\"fds-muted\">次（0 = 不限）</span>");
        b.CloseElement();

        AddHtml(b, ref seq, "<div class=\"fds-sub\">短描述（系统提示词里那一行，重载后生效）</div>");
        TextArea(b, ref seq, toolDescDraft, "工具简介", 2, v => toolDescDraft = v);
        AddHtml(b, ref seq, "<div class=\"fds-sub\">详细说明（调隐式文档时注入的内容）</div>");
        TextArea(b, ref seq, toolExpDraft, "通用规则…", 9, v => toolExpDraft = v);
        b.OpenElement(seq++, "div");
        b.AddAttribute(seq++, "class", "fds-row");
        Button(b, ref seq, "保存工具文档", () =>
        {
            message = Module?.UiSaveToolDocument(toolDescDraft, toolExpDraft, toolLimitDraft) ?? "模块未激活";
            StateHasChanged();
        }, busy);
        Button(b, ref seq, "恢复默认文案", () =>
        {
            toolDescDraft = FeedScrollModule.DefaultToolDescription;
            toolExpDraft = FeedScrollModule.DefaultToolExplanation;
            message = "已把默认文案填回编辑框；点「保存工具文档」才会落盘。";
            StateHasChanged();
        }, busy);
        Button(b, ref seq, "重置今日注入次数", () =>
        {
            message = Module?.UiResetToolDocCounter() ?? "模块未激活";
            StateHasChanged();
        }, busy);
        b.CloseElement();
        AddHtml(b, ref seq, "<div class=\"fds-muted\">函数签名由代码自动生成，不在这里改。关掉注入后，AI 只会收到一行函数名提醒。</div>");
        b.OpenElement(seq++, "details");
        b.AddAttribute(seq++, "class", "fds-details");
        b.OpenElement(seq++, "summary");
        b.AddContent(seq++, "预览当前完整文档");
        b.CloseElement();
        b.OpenElement(seq++, "pre");
        b.AddAttribute(seq++, "class", "fds-pre");
        b.AddContent(seq++, Module?.UiToolDocumentPreview() ?? "模块未激活");
        b.CloseElement();
        b.CloseElement();
        b.CloseElement();

        // 今日操作
        b.OpenElement(seq++, "div");
        b.AddAttribute(seq++, "class", "fds-card fds-card-today");
        AddHtml(b, ref seq, "<div class=\"fds-card-title\">今日操作 <span class=\"fds-muted\">写操作计数</span></div>");
        AddHtml(b, ref seq, $"<div class=\"fds-muted\">{E(Module?.UiTodayStats() ?? "模块未激活")}</div>");
        bool dryRun = Module?.Configuration.CommentDryRun ?? true;
        b.OpenElement(seq++, "div");
        b.AddAttribute(seq++, "class", "fds-row");
        Button(b, ref seq, dryRun ? "关闭演练模式（允许真发）" : "开启演练模式（只预览）", () =>
        {
            if (Module == null) return;
            Module.Configuration.CommentDryRun = !Module.Configuration.CommentDryRun;
            Module.PersistConfiguration();
            message = Module.Configuration.CommentDryRun
                ? "评论/回复演练模式已开启：只预览，不发送。"
                : "评论/回复演练模式已关闭：主人明确确认后才会真发。";
            StateHasChanged();
        }, busy);
        b.CloseElement();
        AddHtml(b, ref seq, $"<div class=\"fds-muted\">当前：{E(dryRun ? "演练中（评论/回复只预览）" : "允许真发（仍需主人明确确认）")}。改动已即时保存。</div>");
        bool postDry = Module?.Configuration.PostDryRun ?? true;
        b.OpenElement(seq++, "div");
        b.AddAttribute(seq++, "class", "fds-row");
        Button(b, ref seq, postDry ? "关闭发帖演练（允许真发）" : "开启发帖演练（只预览）", () =>
        {
            if (Module == null) return;
            Module.Configuration.PostDryRun = !Module.Configuration.PostDryRun;
            Module.PersistConfiguration();
            message = Module.Configuration.PostDryRun
                ? "发帖演练模式已开启：只预览标题/正文/附图，不打开发布页。"
                : "发帖演练模式已关闭：主人明确说“发”后才会真发。";
            StateHasChanged();
        }, busy);
        b.CloseElement();
        int postLimit = Math.Clamp(Module?.Configuration.PostDailyLimit ?? 1, 1, 20);
        AddHtml(b, ref seq, $"<div class=\"fds-muted\">发帖：{E(postDry ? "演练中（只预览，不发送）" : $"允许真发（仍需主人明确确认，每日 {postLimit} 帖）")}。附图只从 images 目录取。</div>");
        b.CloseElement();

        List<FeedItem> recent = Module?.UiRecentItems() ?? new List<FeedItem>();
        b.OpenElement(seq++, "div");
        b.AddAttribute(seq++, "class", "fds-card fds-card-recent");
        AddHtml(b, ref seq, "<div class=\"fds-card-title\">最近刷到的帖子 <span class=\"fds-muted\">只列标题，点「打开」看原帖</span></div>");
        if (recent.Count == 0)
        {
            AddHtml(b, ref seq, "<div class=\"fds-muted\">还没有刷过。点上面的按钮试一次，或让 AI 调用 FeedTiebaList / FeedXhhList。</div>");
        }
        else
        {
            b.OpenElement(seq++, "div");
            b.AddAttribute(seq++, "class", "fds-log");
            foreach (FeedItem item in recent)
            {
                b.OpenElement(seq++, "div");
                b.AddAttribute(seq++, "class", "fds-log-row");
                AddHtml(b, ref seq, $"<span class=\"fds-log-site\">{E(item.Site == "xhh" ? "小黑盒" : "贴吧")}</span>"
                    + $"<span class=\"fds-log-id\">{E(item.Id)}</span>");
                b.OpenElement(seq++, "div");
                b.AddAttribute(seq++, "class", "fds-log-title");
                b.AddContent(seq++, item.Title);
                b.CloseElement();
                Button(b, ref seq, "打开", async () =>
                {
                    busy = true;
                    message = Module == null ? "模块未激活" : await Module.UiOpenItemAsync(item.Id);
                    busy = false;
                    StateHasChanged();
                }, busy);
                b.CloseElement();
            }
            b.CloseElement();
        }
        b.CloseElement();

        b.OpenElement(seq++, "div");
        b.AddAttribute(seq++, "class", "fds-card fds-card-folders");
        AddHtml(b, ref seq, "<div class=\"fds-card-title\">文件夹快捷方式 <span class=\"fds-muted\">收藏图片 / 阅读笔记 / 正文缓存</span></div>");
        b.OpenElement(seq++, "div");
        b.AddAttribute(seq++, "class", "fds-row");
        Button(b, ref seq, "打开图片文件夹", () =>
        {
            message = Module?.UiOpenImagesFolder() ?? "模块未激活";
            StateHasChanged();
        }, busy);
        Button(b, ref seq, "打开阅读笔记", () =>
        {
            message = Module?.UiOpenNotesFolder() ?? "模块未激活";
            StateHasChanged();
        }, busy);
        Button(b, ref seq, "打开正文缓存", () =>
        {
            message = Module?.UiOpenArticlesFolder() ?? "模块未激活";
            StateHasChanged();
        }, busy);
        b.CloseElement();
        b.CloseElement();
        b.CloseElement();

        AddHtml(b, ref seq, $"<div class=\"fds-muted fds-footer\">状态文件：{E(Module?.UiStatePath ?? "")}</div>");
        b.CloseElement();
    }

    void Button(RenderTreeBuilder b, ref int seq, string label, Action handler, bool disabled = false)
        => Button(b, ref seq, label, () => { handler(); return Task.CompletedTask; }, disabled);

    void Button(RenderTreeBuilder b, ref int seq, string label, Func<Task> handler, bool disabled = false)
    {
        b.OpenElement(seq++, "button");
        b.AddAttribute(seq++, "type", "button");
        b.AddAttribute(seq++, "class", "fds-btn");
        if (disabled) b.AddAttribute(seq++, "disabled", true);
        b.AddAttribute(seq++, "onclick", EventCallback.Factory.Create<MouseEventArgs>(this, handler));
        b.AddContent(seq++, label);
        b.CloseElement();
    }

    void TextArea(RenderTreeBuilder b, ref int seq, string value, string placeholder, int rows, Action<string> onChange)
    {
        b.OpenElement(seq++, "textarea");
        b.AddAttribute(seq++, "class", "fds-textarea");
        b.AddAttribute(seq++, "rows", rows);
        b.AddAttribute(seq++, "placeholder", placeholder);
        b.AddAttribute(seq++, "oninput", EventCallback.Factory.Create<ChangeEventArgs>(this, e =>
        {
            onChange(e.Value?.ToString() ?? "");
        }));
        b.AddContent(seq++, value);
        b.CloseElement();
    }

    void NumberInput(RenderTreeBuilder b, ref int seq, string value, string min, string max, string placeholder, Action<string> onChange)
    {
        b.OpenElement(seq++, "input");
        b.AddAttribute(seq++, "class", "fds-input fds-num");
        b.AddAttribute(seq++, "type", "number");
        b.AddAttribute(seq++, "min", min);
        b.AddAttribute(seq++, "max", max);
        b.AddAttribute(seq++, "value", value);
        b.AddAttribute(seq++, "placeholder", placeholder);
        b.AddAttribute(seq++, "oninput", EventCallback.Factory.Create<ChangeEventArgs>(this, e =>
        {
            onChange(e.Value?.ToString() ?? "");
        }));
        b.CloseElement();
    }

    static string E(string? text) => System.Net.WebUtility.HtmlEncode(text ?? "");

    static void AddHtml(RenderTreeBuilder b, ref int seq, string html)
        => b.AddMarkupContent(seq++, html);

    void TextInput(RenderTreeBuilder b, ref int seq, string value, string placeholder, Action<string> onChange)
    {
        b.OpenElement(seq++, "input");
        b.AddAttribute(seq++, "class", "fds-input");
        b.AddAttribute(seq++, "value", value);
        b.AddAttribute(seq++, "placeholder", placeholder);
        b.AddAttribute(seq++, "oninput", EventCallback.Factory.Create<ChangeEventArgs>(this, e =>
        {
            onChange(e.Value?.ToString() ?? "");
        }));
        b.CloseElement();
    }

    const string Css = """
        .fds-root{font-family:"Microsoft YaHei UI",sans-serif;color:#243044;padding:18px 22px;max-width:1280px;margin:0 auto;background:linear-gradient(180deg,#f6f9ff 0,#fff 300px)}
        .fds-header{display:flex;align-items:flex-end;justify-content:space-between;gap:12px;flex-wrap:wrap;margin-bottom:14px}
        .fds-title{font-size:20px;font-weight:700;margin:0 0 2px;background:linear-gradient(90deg,#1d4ed8,#0f766e 55%,#7c3aed);-webkit-background-clip:text;background-clip:text;color:transparent}
        .fds-badges{display:flex;gap:6px;flex-wrap:wrap}
        .fds-badge{font-size:11px;background:#eef4fb;color:#31527a;border:1px solid #dbeafe;border-radius:999px;padding:3px 9px;font-weight:600}
        .fds-badge-list{background:#eef2ff;color:#4338ca;border-color:#e0e7ff}
        .fds-badge-on{background:#ecfdf5;color:#047857;border-color:#d1fae5}
        .fds-badge-off{background:#fff7ed;color:#b45309;border-color:#ffedd5}
        .fds-muted{color:#7b8798;font-size:12px;font-weight:400}
        .fds-msg{margin:8px 0;padding:8px 10px;border-left:3px solid #3b82f6;border-radius:8px;background:linear-gradient(90deg,#eff6ff,#f8fbff);color:#1e40af;white-space:pre-wrap;box-shadow:0 4px 12px rgba(37,99,235,.06)}
        .fds-grid{display:grid;grid-template-columns:minmax(0,1.05fr) minmax(0,.95fr);gap:16px;align-items:start;grid-auto-flow:dense}
        .fds-card{border:1px solid #e2e8f0;border-radius:12px;padding:12px 14px;margin:0;min-width:0;background:linear-gradient(180deg,var(--fds-card-soft,#fff) 0,#fff 120px);box-shadow:inset 0 3px 0 var(--fds-card-accent,#dbeafe),0 6px 18px rgba(30,64,175,.06);transition:box-shadow .15s ease,background .15s ease}
        .fds-card:hover{box-shadow:inset 0 3px 0 var(--fds-card-accent,#dbeafe),0 9px 24px rgba(30,64,175,.11)}
        .fds-card-status{--fds-card-accent:#3b82f6;--fds-card-soft:#f7fbff}
        .fds-card-try{--fds-card-accent:#0d9488;--fds-card-soft:#f7fdfb}
        .fds-card-block{--fds-card-accent:#e11d48;--fds-card-soft:#fff8fa}
        .fds-card-interest{--fds-card-accent:#7c3aed;--fds-card-soft:#fbf9ff}
        .fds-card-auto{--fds-card-accent:#d97706;--fds-card-soft:#fffdf5}
        .fds-card-tooldoc{--fds-card-accent:#4f46e5;--fds-card-soft:#f8f8ff}
        .fds-card-today{--fds-card-accent:#16a34a;--fds-card-soft:#f8fdf9}
        .fds-card-recent{--fds-card-accent:#0284c7;--fds-card-soft:#f7fcff}
        .fds-card-folders{--fds-card-accent:#ea580c;--fds-card-soft:#fffaf5}
        .fds-card-status,.fds-card-try{grid-column:1}
        .fds-card-block,.fds-card-interest{grid-column:2}
        .fds-card-today,.fds-card-recent,.fds-card-folders{grid-column:1/-1}
        .fds-card-auto{grid-column:1/-1}
        .fds-card-tooldoc{grid-column:1/-1}
        .fds-auto-split{display:grid;grid-template-columns:minmax(0,1.15fr) minmax(0,.85fr);gap:18px}
        .fds-textarea{width:100%;box-sizing:border-box;border:1px solid #c7d2fe;border-radius:8px;padding:8px;font-family:inherit;font-size:12px;line-height:1.55;resize:vertical;margin:8px 0 4px;background:#fff}
        .fds-input.fds-num{flex:0 0 88px;min-width:88px}
        .fds-chip-slot{display:inline-flex;align-items:center;gap:4px;background:#fff7ed;color:#b45309;border:1px solid #ffedd5}
        .fds-chip-x{border:0;background:transparent;color:#94a3b8;cursor:pointer;padding:0 2px;font-size:13px;line-height:1}
        .fds-chip-x:hover{color:#e11d48}
        .fds-card-title{font-weight:700;margin-bottom:8px;color:#1e293b}
        .fds-card-status .fds-card-title{color:#1d4ed8}
        .fds-card-try .fds-card-title{color:#0f766e}
        .fds-card-block .fds-card-title{color:#be123c}
        .fds-card-interest .fds-card-title{color:#6d28d9}
        .fds-card-auto .fds-card-title{color:#b45309}
        .fds-card-tooldoc .fds-card-title{color:#4338ca}
        .fds-card-today .fds-card-title{color:#15803d}
        .fds-card-recent .fds-card-title{color:#0369a1}
        .fds-card-folders .fds-card-title{color:#c2410c}
        .fds-details{margin-top:8px}
        .fds-details summary{cursor:pointer;color:#4f46e5;font-size:12px;font-weight:600;padding:4px 0}
        .fds-pre{margin:0;padding:8px;background:#f8fafc;border:1px solid #e2e8f0;border-radius:8px;font-size:12px;max-height:320px;overflow:auto;white-space:pre-wrap}
        .fds-row{display:flex;gap:8px;flex-wrap:wrap;margin-top:8px}
        .fds-btn{border:1px solid #c7d2fe;background:linear-gradient(180deg,#fff,#f5f7ff);border-radius:8px;padding:6px 10px;cursor:pointer;color:#1e3a8a;font-weight:600;transition:background .12s,border-color .12s,box-shadow .12s}
        .fds-btn:hover{background:#eef2ff;border-color:#818cf8;box-shadow:0 2px 8px rgba(99,102,241,.12)}
        .fds-btn:disabled{opacity:.55;cursor:default}
        .fds-input{flex:1;min-width:160px;border:1px solid #c7d2fe;border-radius:8px;padding:6px 8px;background:#fff}
        .fds-input:focus,.fds-textarea:focus{outline:none;border-color:#6366f1;box-shadow:0 0 0 3px rgba(99,102,241,.12)}
        .fds-sub{font-size:11px;color:#475569;font-weight:700;margin:8px 0 4px}
        .fds-card-status .fds-sub,.fds-card-try .fds-sub{color:#64748b}
        .fds-chips{display:flex;gap:5px;flex-wrap:wrap}
        .fds-chip{font-size:11px;background:#eef2ff;color:#4338ca;border:1px solid #e0e7ff;border-radius:999px;padding:2px 8px}
        .fds-item{display:flex;gap:8px;align-items:center;justify-content:space-between;border-bottom:1px dashed #eef2f7;padding:5px 0;font-size:12px;transition:background .12s}
        .fds-item:hover{background:#fafbff}
        .fds-log{display:flex;flex-direction:column;max-height:460px;overflow:auto;padding-right:4px}
        .fds-log-row{display:flex;align-items:center;gap:8px;padding:7px 0;border-bottom:1px dashed #eef2f7}
        .fds-log-row:hover{background:#fafbff}
        .fds-log-site{font-size:11px;background:#eef4fb;color:#31527a;border:1px solid #dbeafe;border-radius:999px;padding:2px 7px;white-space:nowrap}
        .fds-log-id{font-size:11px;color:#6366f1;white-space:nowrap}
        .fds-log-title{flex:1;min-width:0;font-size:13px;line-height:1.45;word-break:break-word}
        .fds-log-row .fds-btn{padding:4px 8px;white-space:nowrap}
        .fds-footer{margin-top:14px}
        @media (max-width:980px){
            .fds-grid{grid-template-columns:1fr}
            .fds-card-status,.fds-card-try,.fds-card-block,.fds-card-interest,.fds-card-today,.fds-card-recent,.fds-card-folders,.fds-card-auto,.fds-card-tooldoc{grid-column:1}
            .fds-auto-split{grid-template-columns:1fr}
        }
        """;
}

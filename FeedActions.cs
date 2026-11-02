using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Alife.Foundation;
using Alife.Function.FunctionCaller;
using Microsoft.Extensions.Logging;

namespace presents.FeedScroll;

/// <summary>
/// 写操作：收藏帖子、保存图片、评论/回复（默认演练）。
/// 全部走真实浏览器点击 + 执行后回读状态，不盲报成功；验证码/登录墙出现就停。
/// </summary>
public partial class FeedScrollModule
{
    readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(20) };
    static readonly Regex PrivacyPattern = new(
        @"(1[3-9]\d{9}|[\w.+-]+@[\w-]+\.[\w.]+|(QQ|微信|电话|手机)\s*[:：]?\s*\S+|(地址|住址)\s*[:：]\s*\S+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    static readonly string[] BlockedWords = { "加我", "私聊", "代练", "出售", "刷单", "返利", "博彩" };

    // ─────────────────────────────────────────────────────────────
    // 收藏
    // ─────────────────────────────────────────────────────────────

    [XmlFunction(FunctionMode.OneShot, "FeedFavorite")]
    [Description("收藏/取消收藏列表里的帖子（动账号，需登录）。id 来自列表，按主人明确指令操作。")]
    public Task<string> FeedFavorite(
        [Description("列表临时 id，如 TB3 / XH2")] string id,
        [Description("true=收藏，false=取消，默认 true")] bool? favorite = true)
    {
        if (!Configuration.EnableWriteActions || !Configuration.AllowFavorite)
            return Task.FromResult("收藏未开启：到控制台打开「允许写操作」与「允许收藏帖子」。");
        FeedItem? item = FindItem(id);
        if (item == null) return Task.FromResult("没有这个 id，先刷一次列表。");
        if (IsItemBlocked(item)) return Task.FromResult($"「{item.Title}」命中屏蔽规则，不操作。");
        string itemUrl = FeedUrls.Normalize(item.Url, item.Site);
        bool want = favorite ?? true;
        EnsureActionDay();
        if (state.FavoriteCountToday >= Math.Clamp(Configuration.FavoriteDailyLimit, 1, 200))
            return Task.FromResult($"今天收藏次数已达上限（{Configuration.FavoriteDailyLimit}）。");

        return WithBrowser(async host =>
        {
            FeedListResult nav = await host.NavigateAsync(itemUrl, 20).ConfigureAwait(false);
            if (!nav.Success) return "页面打不开：" + (nav.TimedOut ? "超时" : nav.Error) + "；可能需要登录或验证。";
            await Task.Delay(900).ConfigureAwait(false);

            string before = await host.EvalAsync(FavoriteStateScript).ConfigureAwait(false);
            bool collected = before.Contains("\"collected\":true", StringComparison.Ordinal);
            if (collected == want)
                return $"这条已经是「{(want ? "已收藏" : "未收藏")}」状态，不用重复操作。";

            string clicked = await host.EvalAsync(FavoriteClickScript.Replace("__WANT__", want ? "true" : "false")).ConfigureAwait(false);
            if (clicked.Contains("\"ok\":false", StringComparison.Ordinal))
                return "没找到收藏按钮：可能未登录、页面结构变了，或需要先人工完成验证。请打开浏览器窗口检查。";
            await Task.Delay(1300).ConfigureAwait(false);
            string after = await host.EvalAsync(FavoriteStateScript).ConfigureAwait(false);
            bool nowCollected = after.Contains("\"collected\":true", StringComparison.Ordinal);
            if (nowCollected != want)
                return "点了收藏，但状态没变成预期值：可能未登录/被风控/按钮是二级菜单。请打开浏览器窗口确认。";

            state.FavoriteCountToday++;
            SaveState();
            return $"已{(want ? "收藏" : "取消收藏")}：{item.Title}\n（今日收藏操作 {state.FavoriteCountToday}/{Configuration.FavoriteDailyLimit}）";
        });
    }

    // ─────────────────────────────────────────────────────────────
    // 保存图片
    // ─────────────────────────────────────────────────────────────

    [XmlFunction(FunctionMode.OneShot, "FeedSaveImages")]
    [Description("把帖子里的大图存到本地（不是账号收藏）。id 来自列表，按主人明确指令操作。")]
    public Task<string> FeedSaveImages(
        [Description("列表临时 id")] string id,
        [Description("最多几张，留空用配置上限")] int? max = null)
    {
        if (!Configuration.EnableWriteActions || !Configuration.AllowSaveImages)
            return Task.FromResult("保存图片未开启：到控制台打开「允许写操作」与「允许保存帖子图片」。");
        FeedItem? item = FindItem(id);
        if (item == null) return Task.FromResult("没有这个 id，先刷一次列表。");
        if (IsItemBlocked(item)) return Task.FromResult($"「{item.Title}」命中屏蔽规则，不操作。");
        string itemUrl = FeedUrls.Normalize(item.Url, item.Site);
        int limit = Math.Clamp(max ?? Configuration.SaveImagesMax, 1, 20);

        return WithBrowser(async host =>
        {
            FeedListResult nav = await host.NavigateAsync(itemUrl, 20).ConfigureAwait(false);
            if (!nav.Success) return NavError(nav);
            await Task.Delay(900).ConfigureAwait(false);
            await host.EvalAsync("window.scrollTo(0, document.body.scrollHeight); 'ok'").ConfigureAwait(false);
            await Task.Delay(800).ConfigureAwait(false);
            string json = await host.EvalAsync(ImageScript.Replace("__LIMIT__", limit.ToString())).ConfigureAwait(false);
            List<string> urls;
            try { urls = JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>(); }
            catch { urls = new List<string>(); }
            if (urls.Count == 0) return "没有找到足够大的图片（可能全是头像/表情，或页面没加载完）。";

            string folder = Path.Combine(AlifePath.StorageFolderPath, "PluginData", "presents.FeedScroll", "images", DateTime.Now.ToString("yyyyMMdd"));
            Directory.CreateDirectory(folder);
            var saved = new List<string>();
            var failed = new List<string>();
            int index = 0;
            foreach (string url in urls.Take(limit))
            {
                index++;
                try
                {
                    var request = new HttpRequestMessage(HttpMethod.Get, url);
                    request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36");
                    request.Headers.Referrer = new Uri(itemUrl);
                    using HttpResponseMessage response = await http.SendAsync(request).ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode) { failed.Add($"#{index} HTTP {response.StatusCode}"); continue; }
                    byte[] bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                    if (bytes.Length < 30000) { failed.Add($"#{index} 太小"); continue; }
                    if (bytes.Length > 20 * 1024 * 1024) { failed.Add($"#{index} 超过 20MB"); continue; }
                    string ext = GuessExtension(response.Content.Headers.ContentType?.MediaType, url);
                    string name = $"{item.Site}-{DateTime.Now:HHmmssfff}-{index}{ext}";
                    string path = Path.Combine(folder, name);
                    await File.WriteAllBytesAsync(path, bytes).ConfigureAwait(false);
                    saved.Add(name);
                }
                catch (Exception e)
                {
                    failed.Add($"#{index} {e.Message}");
                }
            }
            state.LastImageSaveAt = DateTime.Now;
            state.LastSavedImages = saved.Count;
            SaveState();
            var sb = new StringBuilder();
            sb.AppendLine($"从「{item.Title}」保存了 {saved.Count} 张图：");
            foreach (string name in saved) sb.AppendLine("- " + name);
            sb.AppendLine($"目录：{folder}");
            if (failed.Count > 0)
            {
                sb.AppendLine($"另有 {failed.Count} 张失败：{string.Join("；", failed.Take(4))}");
                sb.AppendLine("（不是账号收藏，只是本地文件；需要登录的图可能下载失败。）");
            }
            return sb.ToString().TrimEnd();
        });
    }

    // ─────────────────────────────────────────────────────────────
    // 评论 / 回复（默认演练）
    // ─────────────────────────────────────────────────────────────

    [XmlFunction(FunctionMode.OneShot, "FeedComment")]
    [Description("给帖子发评论；默认演练，只返回将要发布的内容。只有主人明确说“发”且关闭演练模式才真发。")]
    public Task<string> FeedComment(
        [Description("列表临时 id")] string id,
        [Description("评论正文，1~200 字，不含隐私")] string text,
        [Description("主人明确要求发送才填 true；演练中即使 true 也不发")] bool? confirm = false)
    {
        if (!Configuration.EnableWriteActions || !Configuration.AllowComment)
            return Task.FromResult("评论未开启：到控制台打开「允许写操作」与「允许评论/回复」。");
        FeedItem? item = FindItem(id);
        if (item == null) return Task.FromResult("没有这个 id，先刷一次列表。");
        if (IsItemBlocked(item)) return Task.FromResult($"「{item.Title}」命中屏蔽规则，不操作。");
        string itemUrl = FeedUrls.Normalize(item.Url, item.Site);
        string body = (text ?? "").Trim();
        string? invalid = ValidateComment(body);
        if (invalid != null) return Task.FromResult(invalid);

        string preview = BuildPreview("评论", item, body, null);
        if (Configuration.CommentDryRun || !(confirm ?? false))
            return Task.FromResult(preview + "\n（演练模式：没有发送。主人明确说“发”并在控制台关闭演练后，我再执行。）");

        EnsureActionDay();
        if (state.CommentCountToday >= Math.Clamp(Configuration.CommentDailyLimit, 1, 50))
            return Task.FromResult($"今天评论次数已达上限（{Configuration.CommentDailyLimit}）。");
        string fingerprint = Fingerprint(body);
        if (state.CommentFingerprints.Contains(fingerprint))
            return Task.FromResult("这段评论今天已经发过，换一句再说，避免刷屏。");

        return WithBrowser(async host =>
        {
            FeedListResult nav = await host.NavigateAsync(itemUrl, 20).ConfigureAwait(false);
            if (!nav.Success) return NavError(nav);
            await Task.Delay(900).ConfigureAwait(false);
            string filled = await host.EvalAsync(CommentFillScript.Replace("__TEXT__", JsonSerializer.Serialize(body))).ConfigureAwait(false);
            if (filled.Contains("\"ok\":false", StringComparison.Ordinal))
                return "没找到评论框/发送按钮：可能未登录或需要验证。请打开浏览器窗口人工发，不要硬试。";
            await Task.Delay(1500).ConfigureAwait(false);
            string verify = await host.EvalAsync(VerifyCommentScript.Replace("__SNIPPET__", JsonSerializer.Serialize(body[..Math.Min(12, body.Length)]))).ConfigureAwait(false);
            state.CommentCountToday++;
            state.CommentFingerprints.Add(fingerprint);
            if (state.CommentFingerprints.Count > 100) state.CommentFingerprints.RemoveRange(0, state.CommentFingerprints.Count - 100);
            SaveState();
            bool present = verify.Contains("\"present\":true", StringComparison.Ordinal);
            return present
                ? $"已发送评论（今日 {state.CommentCountToday}/{Configuration.CommentDailyLimit}）：{body}"
                : "已点击发送，但页面里暂时没看到这条内容：可能还在审核/被折叠，或发送失败。请打开浏览器窗口确认，别重复发。";
        });
    }

    [XmlFunction(FunctionMode.OneShot, "FeedReply")]
    [Description("回复某条热评（楼中楼）；默认演练，只有主人明确说“发”且关闭演练模式才真发。")]
    public Task<string> FeedReply(
        [Description("列表临时 id")] string id,
        [Description("要回复的评论原文（从 FeedOpen 热评复制）")] string target,
        [Description("回复正文，1~200 字")] string text,
        [Description("主人明确要求发送才填 true")] bool? confirm = false)
    {
        if (!Configuration.EnableWriteActions || !Configuration.AllowComment)
            return Task.FromResult("回复未开启：到控制台打开「允许写操作」与「允许评论/回复」。");
        FeedItem? item = FindItem(id);
        if (item == null) return Task.FromResult("没有这个 id，先刷一次列表。");
        if (IsItemBlocked(item)) return Task.FromResult($"「{item.Title}」命中屏蔽规则，不操作。");
        string itemUrl = FeedUrls.Normalize(item.Url, item.Site);
        string snippet = (target ?? "").Trim();
        if (snippet.Length < 4) return Task.FromResult("请给出要回复的那条评论的一段原文（至少 4 个字）。");
        string body = (text ?? "").Trim();
        string? invalid = ValidateComment(body);
        if (invalid != null) return Task.FromResult(invalid);

        string preview = BuildPreview("回复", item, body, snippet);
        if (Configuration.CommentDryRun || !(confirm ?? false))
            return Task.FromResult(preview + "\n（演练模式：没有发送。主人明确说“发”并在控制台关闭演练后，我再执行。）");

        EnsureActionDay();
        if (state.CommentCountToday >= Math.Clamp(Configuration.CommentDailyLimit, 1, 50))
            return Task.FromResult($"今天评论次数已达上限（{Configuration.CommentDailyLimit}）。");

        return WithBrowser(async host =>
        {
            FeedListResult nav = await host.NavigateAsync(itemUrl, 20).ConfigureAwait(false);
            if (!nav.Success) return NavError(nav);
            await Task.Delay(900).ConfigureAwait(false);
            string located = await host.EvalAsync(ReplyLocateScript.Replace("__TARGET__", JsonSerializer.Serialize(snippet))).ConfigureAwait(false);
            if (located.Contains("\"ok\":false", StringComparison.Ordinal))
                return "没找到那条评论或它的「回复」按钮：可能被折叠/需要登录。请打开浏览器窗口人工回，别硬试。";
            await Task.Delay(900).ConfigureAwait(false);
            string filled = await host.EvalAsync(CommentFillScript.Replace("__TEXT__", JsonSerializer.Serialize(body))).ConfigureAwait(false);
            if (filled.Contains("\"ok\":false", StringComparison.Ordinal))
                return "已点开回复，但没找到输入框/发送按钮；请人工处理。";
            await Task.Delay(1500).ConfigureAwait(false);
            state.CommentCountToday++;
            state.CommentFingerprints.Add(Fingerprint(body));
            SaveState();
            return $"已尝试回复「{snippet}」（今日 {state.CommentCountToday}/{Configuration.CommentDailyLimit}）：{body}\n（请打开浏览器窗口确认是否发出。）";
        });
    }

    // ─────────────────────────────────────────────────────────────
    // 公共辅助
    // ─────────────────────────────────────────────────────────────

    FeedItem? FindItem(string id)
    {
        string key = (id ?? "").Trim().ToUpperInvariant();
        return state.LastItems.FirstOrDefault(x => string.Equals(x.Id, key, StringComparison.OrdinalIgnoreCase));
    }

    void EnsureActionDay()
    {
        string today = DateTime.Now.ToString("yyyy-MM-dd");
        state.CommentFingerprints ??= new List<string>();
        state.PostFingerprints ??= new List<string>();
        if (state.FavoriteDayKey != today)
        {
            state.FavoriteDayKey = today;
            state.FavoriteCountToday = 0;
        }
        if (state.CommentDayKey != today)
        {
            state.CommentDayKey = today;
            state.CommentCountToday = 0;
            state.CommentFingerprints.Clear();
        }
        if (state.PostDayKey != today)
        {
            state.PostDayKey = today;
            state.PostCountToday = 0;
            state.PostFingerprints.Clear();
        }
    }

    async Task<string> WithBrowser(Func<FeedWebHost, Task<string>> action)
    {
        FeedWebHost? host = await EnsureBrowserAsync().ConfigureAwait(false);
        if (host == null) return "浏览器不可用：" + (string.IsNullOrWhiteSpace(lastStatus) ? "请打开控制台查看日志。" : lastStatus);
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            TimeSpan wait = TimeSpan.FromSeconds(Math.Max(0, Configuration.ActionIntervalSeconds)) - (DateTime.Now - state.LastActionAt);
            if (wait > TimeSpan.Zero) await Task.Delay(wait).ConfigureAwait(false);
            state.LastActionAt = DateTime.Now;
            return await action(host).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "贴吧/小黑盒插件：写操作失败");
            lastStatus = e.Message;
            return "操作失败：" + e.Message;
        }
        finally
        {
            gate.Release();
        }
    }

    string? ValidateComment(string body)
    {
        if (body.Length < 1) return "评论内容为空。";
        if (body.Length > 200) return "评论太长（上限 200 字）。";
        if (PrivacyPattern.IsMatch(body)) return "评论里可能有手机号/邮箱/QQ/微信/地址等隐私，先删掉再发。";
        if (BlockedWords.Any(w => body.Contains(w, StringComparison.OrdinalIgnoreCase)))
            return "评论里含广告/导流词（加我、私聊、代练、出售等），不发。";
        int links = Regex.Matches(body, @"https?://", RegexOptions.IgnoreCase).Count;
        if (links > 1) return "评论里最多带一个链接。";
        return null;
    }

    string BuildPreview(string action, FeedItem item, string body, string? target)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"[{action}预览]");
        sb.AppendLine($"- 目标：{item.Site} · {item.Title}");
        sb.AppendLine($"- 链接：{FeedUrls.Normalize(item.Url, item.Site)}");
        if (!string.IsNullOrWhiteSpace(target)) sb.AppendLine($"- 回复对象：{target}");
        sb.AppendLine($"- 内容：{body}");
        sb.AppendLine($"- 当前模式：{(Configuration.CommentDryRun ? "演练（不发送）" : "可发送")}");
        return sb.ToString().TrimEnd();
    }

    static string Fingerprint(string text)
    {
        unchecked
        {
            uint hash = 2166136261;
            foreach (char c in Regex.Replace(text, @"\s+", ""))
                hash = (hash ^ c) * 16777619;
            return (hash & 0xFFFFFFFF).ToString("x8");
        }
    }

    static string NavError(FeedListResult nav)
        => nav.TimedOut ? "页面加载超时" : ("页面打不开：" + nav.Error);

    static string GuessExtension(string? contentType, string url)
    {
        string type = (contentType ?? "").ToLowerInvariant();
        if (type.Contains("png")) return ".png";
        if (type.Contains("webp")) return ".webp";
        if (type.Contains("gif")) return ".gif";
        string path = url.Split('?')[0].ToLowerInvariant();
        foreach (string ext in new[] { ".png", ".jpg", ".jpeg", ".webp", ".gif" })
            if (path.EndsWith(ext, StringComparison.Ordinal)) return ext == ".jpeg" ? ".jpg" : ext;
        return ".jpg";
    }

    // ─────────────────────────────────────────────────────────────
    // 页面脚本
    // ─────────────────────────────────────────────────────────────

    const string FavoriteStateScript = """
        (() => {
          const labels = [...document.querySelectorAll('a,button,div,span')]
            .map(e => ({ e, t: (e.innerText || '').replace(/\s+/g, ' ').trim() }))
            .filter(x => x.t === '收藏' || x.t === '已收藏' || x.t === '取消收藏');
          if (!labels.length) return JSON.stringify({ found: false, collected: false, label: '' });
          labels.sort((a, b) => a.t.length - b.t.length);
          const label = labels[0].t;
          return JSON.stringify({ found: true, collected: label !== '收藏', label });
        })()
        """;

    const string FavoriteClickScript = """
        (() => {
          const want = __WANT__;
          const labels = [...document.querySelectorAll('a,button,div,span')]
            .map(e => ({ e, t: (e.innerText || '').replace(/\s+/g, ' ').trim() }))
            .filter(x => want ? (x.t === '收藏') : (x.t === '已收藏' || x.t === '取消收藏'));
          if (!labels.length) return JSON.stringify({ ok: false, reason: 'no-button' });
          labels.sort((a, b) => a.t.length - b.t.length);
          const target = labels[0].e.closest('a,button') || labels[0].e;
          target.click();
          return JSON.stringify({ ok: true, label: labels[0].t });
        })()
        """;

    const string ImageScript = """
        (() => {
          const out = [];
          const seen = new Set();
          for (const img of document.querySelectorAll('img')) {
            const src = img.currentSrc || img.src || '';
            if (!/^https?:/i.test(src)) continue;
            const w = img.naturalWidth || 0;
            const h = img.naturalHeight || 0;
            if (w < 300 || h < 200) continue;
            if (seen.has(src)) continue;
            seen.add(src);
            out.push(src);
            if (out.length >= __LIMIT__) break;
          }
          return JSON.stringify(out);
        })()
        """;

    const string CommentFillScript = """
        (() => {
          const TEXT = __TEXT__;
          const visible = e => {
            const s = getComputedStyle(e);
            return s.display !== 'none' && s.visibility !== 'hidden' && e.offsetParent !== null;
          };
          const inputs = [...document.querySelectorAll('textarea, [contenteditable="true"], input[type="text"]')]
            .filter(visible);
          if (!inputs.length) return JSON.stringify({ ok: false, reason: 'no-input' });
          const input = inputs.find(e => e.tagName === 'TEXTAREA' && !e.disabled)
            || inputs.find(e => e.getAttribute('contenteditable') === 'true');
          if (!input) return JSON.stringify({ ok: false, reason: 'no-textarea' });
          input.focus();
          if (input.tagName === 'TEXTAREA' || input.tagName === 'INPUT') {
            const proto = input.tagName === 'TEXTAREA' ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype;
            const setter = Object.getOwnPropertyDescriptor(proto, 'value').set;
            setter.call(input, TEXT);
            input.dispatchEvent(new Event('input', { bubbles: true }));
            input.dispatchEvent(new Event('change', { bubbles: true }));
          } else {
            input.textContent = TEXT;
            input.dispatchEvent(new InputEvent('input', { bubbles: true, data: TEXT, inputType: 'insertText' }));
          }
          const labels = /^(发送|发布|发表|评论|回复|提交)$/;
          const buttons = [...document.querySelectorAll('button,a,div,span')]
            .filter(e => visible(e) && labels.test((e.innerText || '').replace(/\s+/g, ' ').trim()));
          if (!buttons.length) return JSON.stringify({ ok: false, reason: 'no-submit', filled: true });
          buttons.sort((a, b) => (a.innerText || '').trim().length - (b.innerText || '').trim().length);
          const btn = buttons[0].closest('button,a') || buttons[0];
          btn.click();
          return JSON.stringify({ ok: true, label: (buttons[0].innerText || '').trim() });
        })()
        """;

    const string ReplyLocateScript = """
        (() => {
          const snippet = __TARGET__;
          const visible = e => {
            const s = getComputedStyle(e);
            return s.display !== 'none' && s.visibility !== 'hidden' && e.offsetParent !== null;
          };
          const boxes = [...document.querySelectorAll('div,li,article,section,p')]
            .filter(e => visible(e) && (e.innerText || '').includes(snippet))
            .sort((a, b) => (a.innerText || '').length - (b.innerText || '').length);
          if (!boxes.length) return JSON.stringify({ ok: false, reason: 'no-target' });
          const box = boxes[0];
          const replies = [...box.querySelectorAll('button,a,div,span')]
            .filter(e => visible(e) && (e.innerText || '').trim() === '回复');
          if (!replies.length) return JSON.stringify({ ok: false, reason: 'no-reply-button' });
          const target = replies[0].closest('button,a') || replies[0];
          target.click();
          return JSON.stringify({ ok: true });
        })()
        """;

    const string VerifyCommentScript = """
        (() => {
          const snippet = __SNIPPET__;
          const present = (document.body?.innerText || '').includes(snippet);
          return JSON.stringify({ present });
        })()
        """;
}

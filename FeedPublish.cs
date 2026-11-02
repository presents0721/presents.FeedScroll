using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Alife.Foundation;
using Alife.Function.FunctionCaller;
using Microsoft.Extensions.Logging;

namespace presents.FeedScroll;

/// <summary>
/// 发新帖（默认演练）：标题、正文、本地附图。
/// 和评论一样走真实发布页 + 执行后回读；验证码/登录墙出现就停。
/// 图片只从 PluginData/presents.FeedScroll/images 取，避免把任意本地文件传出去。
/// </summary>
public partial class FeedScrollModule
{
    static readonly string[] PostImageExtensions = { ".jpg", ".jpeg", ".png", ".webp", ".gif" };

    static readonly Regex PostPrivacyPattern = new(
        @"(1[3-9]\d{9}|[\w.+-]+@[\w-]+\.[\w.]+|(QQ|微信)\s*[:：]\s*\S+|(电话|手机|地址|住址)\s*[:：]\s*\S+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    [XmlFunction(FunctionMode.OneShot, "FeedPublish")]
    [Description("发新帖（动账号）：贴吧需要吧名，小黑盒可只给标题/正文。默认演练，只返回预览；只有主人明确说“发”且关闭发帖演练才真发。附图只从插件 images 目录取。")]
    public Task<string> FeedPublish(
        [Description("tieba/xhh，默认 tieba")] string? site = "tieba",
        [Description("贴吧吧名，如 电脑装机；小黑盒可空")] string? forum = null,
        [Description("帖子标题；贴吧必填，小黑盒可空")] string? title = null,
        [Description("正文，1~2000 字，不含隐私")] string? text = null,
        [Description("附图：images 目录下的相对路径或文件名，逗号分隔；最多 4 张")] string? images = null,
        [Description("主人明确要求发送才填 true；演练中即使 true 也不发")] bool? confirm = false)
    {
        if (!Configuration.Enable || !Configuration.EnableWriteActions || !Configuration.AllowPost)
            return Task.FromResult("发帖未开启：到控制台打开「启用贴吧/小黑盒抓取」「允许写操作」与「允许发新帖」。");

        string mode = NormalizePostSite(site);
        if (mode.Length == 0) return Task.FromResult("site 只支持 tieba / xhh。");

        string forumName = mode == "tieba" ? NormalizeForum(forum ?? "") : (forum ?? "").Trim();
        if (mode == "tieba" && forumName.Length == 0)
            return Task.FromResult("贴吧发帖需要吧名（forum），比如 forum=\"电脑装机\"。");
        if (mode == "tieba" && IsForumBlocked(forumName))
            return Task.FromResult($"「{forumName}」在屏蔽列表里，不发。");

        string titleText = (title ?? "").Trim().Replace("\r", " ").Replace("\n", " ");
        string body = (text ?? "").Trim();
        int titleMax = Math.Clamp(Configuration.PostTitleMaxChars, 5, 120);
        int bodyMax = Math.Clamp(Configuration.PostMaxChars, 20, 5000);
        if (body.Length == 0) return Task.FromResult("帖子正文为空。");
        if (body.Length > bodyMax) return Task.FromResult($"正文太长（上限 {bodyMax} 字）。");
        if (titleText.Length > titleMax) return Task.FromResult($"标题太长（上限 {titleMax} 字）。");
        if (mode == "tieba" && titleText.Length == 0) return Task.FromResult("贴吧发帖需要标题。");
        string? invalid = ValidatePostText(titleText, body);
        if (invalid != null) return Task.FromResult(invalid);

        int maxImages = Math.Clamp(Configuration.PostMaxImages, 0, 8);
        List<string> imagePaths = ResolvePostImages(images, maxImages, out string imageError);
        if (imageError.Length > 0) return Task.FromResult(imageError);

        string preview = BuildPostPreview(mode, forumName, titleText, body, imagePaths);
        if (Configuration.PostDryRun || !(confirm ?? false))
            return Task.FromResult(preview + "\n（演练模式：没有打开发布页、没有发送。主人明确说“发”并在控制台关闭「发帖演练」后，我再执行。）");

        EnsureActionDay();
        int limit = Math.Clamp(Configuration.PostDailyLimit, 1, 20);
        if (state.PostCountToday >= limit)
            return Task.FromResult($"今天发帖次数已达上限（{limit}）。");
        string fingerprint = Fingerprint(titleText + "\n" + body);
        if (state.PostFingerprints.Contains(fingerprint))
            return Task.FromResult("这帖今天已经发过，别重复发。");

        string composerUrl = BuildComposerUrl(mode, forumName);
        if (composerUrl.Length == 0)
            return Task.FromResult("发帖入口没有配置。到配置区填「贴吧发帖入口」/「小黑盒发帖入口」。");
        if (!IsAllowed(composerUrl))
            return Task.FromResult("发帖入口不在白名单站点里：" + composerUrl);

        return WithBrowser(async host =>
        {
            FeedListResult nav = await host.NavigateAsync(composerUrl, 25).ConfigureAwait(false);
            if (!nav.Success) return NavError(nav) + "；可能需要登录或人工完成验证。";
            await Task.Delay(1400).ConfigureAwait(false);

            string probe = await WaitForComposerAsync(host).ConfigureAwait(false);
            for (int i = 0; i < 3 && !ComposerReady(probe); i++)
            {
                string opened = await host.EvalAsync(OpenComposerScript).ConfigureAwait(false);
                if (opened.Contains("\"ok\":false", StringComparison.Ordinal)) break;
                await Task.Delay(1600).ConfigureAwait(false);
                probe = await WaitForComposerAsync(host).ConfigureAwait(false);
            }
            if (!ComposerReady(probe))
            {
                string wall = await host.EvalAsync(PostWallScript).ConfigureAwait(false);
                if (wall.Contains("verify", StringComparison.Ordinal))
                    return "发布页要求安全验证。请打开浏览器窗口人工过一次，再重新发。";
                if (wall.Contains("login", StringComparison.Ordinal))
                    return "发布页要求登录/扫码。请打开浏览器窗口登录一次，再重新发。";
                return "没找到标题/正文编辑框：可能未登录、页面结构变了，或该站点网页版不支持发帖。"
                       + "请先在控制台打开浏览器窗口确认能进发布页；知道直接发布页的话，填到配置「贴吧/小黑盒发帖入口」。";
            }

            string filled = await host.EvalAsync(FillPostScript
                .Replace("__TITLE__", JsonSerializer.Serialize(titleText))
                .Replace("__BODY__", JsonSerializer.Serialize(body))).ConfigureAwait(false);
            if (filled.Contains("\"ok\":false", StringComparison.Ordinal))
                return "找到了发布页，但正文没填进去（" + ReadReason(filled) + "）。没有发送，请打开浏览器查看。";

            if (imagePaths.Count > 0)
            {
                string payload = BuildImagePayload(imagePaths, out string payloadError);
                if (payloadError.Length > 0) return payloadError;
                int imagesBefore = ReadCount(await host.EvalAsync(EditorImageCountScript).ConfigureAwait(false));
                string attached = await host.EvalAsync(AttachImagesScript.Replace("__FILES__", payload)).ConfigureAwait(false);
                if (attached.Contains("\"ok\":false", StringComparison.Ordinal))
                    return "图片没能附上（发布页没有可用的图片入口），这帖没有发。"
                           + "可以去掉 images 先发文字，或打开浏览器窗口人工传图。";
                await Task.Delay(4500).ConfigureAwait(false);
                int imagesAfter = ReadCount(await host.EvalAsync(EditorImageCountScript).ConfigureAwait(false));
                if (imagesAfter <= imagesBefore)
                    return "图片没有出现在发布页（上传可能失败/还在转圈），这帖没有发。"
                           + "请打开浏览器窗口确认；也可以去掉 images 先发文字。";
            }

            string clicked = await host.EvalAsync(PublishClickScript).ConfigureAwait(false);
            if (clicked.Contains("\"ok\":false", StringComparison.Ordinal))
                return "正文已填好，但没找到可点的「发布/发表」按钮（可能被禁用或页面结构变了）。没有发送，请人工确认。";
            await Task.Delay(2600).ConfigureAwait(false);

            state.PostCountToday++;
            state.PostFingerprints.Add(fingerprint);
            if (state.PostFingerprints.Count > 50)
                state.PostFingerprints.RemoveRange(0, state.PostFingerprints.Count - 50);
            SaveState();

            string titleSnippet = titleText.Length > 30 ? titleText[..30] : titleText;
            string bodySnippet = body.Length > 30 ? body[..30] : body;
            string verify = await host.EvalAsync(VerifyPublishScript
                .Replace("__TITLE__", JsonSerializer.Serialize(titleSnippet))
                .Replace("__BODY__", JsonSerializer.Serialize(bodySnippet))).ConfigureAwait(false);
            bool present = verify.Contains("\"present\":true", StringComparison.Ordinal)
                           || verify.Contains("\"posted\":true", StringComparison.Ordinal)
                           || verify.Contains("发布成功", StringComparison.Ordinal)
                           || verify.Contains("发表成功", StringComparison.Ordinal);
            string imageNote = imagePaths.Count > 0 ? $" · 附图 {imagePaths.Count} 张" : "";
            return present
                ? $"已发帖（今日 {state.PostCountToday}/{limit}）：{titleText}{imageNote}\n请打开浏览器窗口确认最终效果（可能仍在审核）。"
                : $"已点击发布（今日 {state.PostCountToday}/{limit}），但页面里暂时没看到帖子：可能还在审核/被折叠，或发送失败。请打开浏览器窗口确认，别重复发。";
        });
    }

    static string NormalizePostSite(string? raw)
    {
        string text = (raw ?? "").Trim().ToLowerInvariant();
        return text switch
        {
            "xhh" or "小黑盒" or "heybox" => "xhh",
            "tieba" or "贴吧" or "百度贴吧" or "" => "tieba",
            _ => ""
        };
    }

    string? ValidatePostText(string title, string body)
    {
        string all = title + "\n" + body;
        if (PostPrivacyPattern.IsMatch(all))
            return "标题/正文里可能有手机号/邮箱/QQ/微信/地址等隐私，先删掉再发。";
        if (BlockedWords.Any(w => all.Contains(w, StringComparison.OrdinalIgnoreCase)))
            return "标题/正文里含广告/导流词（加我、私聊、代练、出售等），不发。";
        int links = Regex.Matches(all, @"https?://", RegexOptions.IgnoreCase).Count;
        if (links > 3) return "帖子里最多带三个链接。";
        return null;
    }

    List<string> ResolvePostImages(string? raw, int max, out string error)
    {
        error = "";
        var result = new List<string>();
        string root = Path.GetFullPath(Path.Combine(AlifePath.StorageFolderPath, "PluginData", "presents.FeedScroll", "images"));
        Directory.CreateDirectory(root);
        int limitMb = Math.Clamp(Configuration.PostImageMaxMB, 1, 20);
        foreach (string piece in (raw ?? "").Split(new[] { ',', '，', ';', '；', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string name = piece.Trim().Trim('"');
            if (name.Length == 0) continue;
            if (result.Count >= max)
            {
                error = $"附图最多 {max} 张。";
                return result;
            }

            string full = "";
            if (Path.IsPathRooted(name))
            {
                full = Path.GetFullPath(name);
            }
            else
            {
                string rel = Path.GetFullPath(Path.Combine(root, name));
                if (File.Exists(rel)) full = rel;
                else
                {
                    string file = Path.GetFileName(name);
                    full = Directory.EnumerateFiles(root, file, SearchOption.AllDirectories).FirstOrDefault() ?? "";
                }
            }

            if (full.Length == 0 || !File.Exists(full))
            {
                error = $"找不到图片：{name}（只从插件 images 目录取）。";
                return result;
            }
            if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(full, root, StringComparison.OrdinalIgnoreCase))
            {
                error = $"图片不在插件 images 目录里，拒绝发送：{name}";
                return result;
            }

            string ext = Path.GetExtension(full).ToLowerInvariant();
            if (!PostImageExtensions.Contains(ext))
            {
                error = $"不是支持的图片格式：{name}";
                return result;
            }
            long size = new FileInfo(full).Length;
            if (size > limitMb * 1024L * 1024L)
            {
                error = $"{Path.GetFileName(full)} 超过 {limitMb}MB，先压缩再发。";
                return result;
            }
            if (size < 1024)
            {
                error = $"{Path.GetFileName(full)} 太小，可能不是有效图片。";
                return result;
            }
            result.Add(full);
        }
        return result;
    }

    string BuildImagePayload(List<string> paths, out string error)
    {
        error = "";
        int limitMb = Math.Clamp(Configuration.PostImageMaxMB, 1, 20);
        long totalBytes = 0;
        var list = new List<object>();
        foreach (string path in paths)
        {
            try
            {
                var info = new FileInfo(path);
                if (info.Length > limitMb * 1024L * 1024L)
                {
                    error = $"{info.Name} 超过 {limitMb}MB，先压缩再发。";
                    return "";
                }
                byte[] bytes = File.ReadAllBytes(path);
                totalBytes += bytes.Length;
                if (totalBytes > 12L * 1024 * 1024)
                {
                    error = "附图总大小超过 12MB，减少几张或压缩后再发。";
                    return "";
                }
                list.Add(new
                {
                    name = info.Name,
                    mime = MimeOfImage(path),
                    b64 = Convert.ToBase64String(bytes)
                });
            }
            catch (Exception e)
            {
                error = "读取图片失败：" + e.Message;
                return "";
            }
        }
        return JsonSerializer.Serialize(list);
    }

    static string MimeOfImage(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            _ => "image/jpeg"
        };
    }

    string BuildComposerUrl(string mode, string forumName)
    {
        string template = mode == "xhh" ? Configuration.PostComposerXhh : Configuration.PostComposerTieba;
        template = (template ?? "").Trim();
        if (template.Length == 0) return "";
        if (mode == "tieba")
        {
            if (template.Contains("{forum}", StringComparison.Ordinal))
                template = template.Replace("{forum}", Uri.EscapeDataString(forumName), StringComparison.Ordinal);
            else
                template += (template.Contains("?", StringComparison.Ordinal) ? "&" : "?") + "kw=" + Uri.EscapeDataString(forumName);
        }
        return template;
    }

    string BuildPostPreview(string mode, string forumName, string title, string body, List<string> images)
    {
        var sb = new StringBuilder();
        sb.AppendLine("[发帖预览]");
        sb.AppendLine($"- 站点：{(mode == "xhh" ? "小黑盒" : "百度贴吧")}");
        if (mode == "tieba") sb.AppendLine($"- 吧：{forumName}");
        sb.AppendLine($"- 标题：{(title.Length > 0 ? title : "（无，小黑盒可空）")}");
        string shown = body.Length > 400 ? body[..400] + "…" : body;
        sb.AppendLine($"- 正文：{shown}");
        sb.AppendLine(images.Count == 0
            ? "- 附图：无"
            : $"- 附图：{images.Count} 张 · {string.Join("、", images.Select(Path.GetFileName))}");
        sb.AppendLine($"- 当前模式：{(Configuration.PostDryRun ? "演练（不发送）" : "可发送")}");
        return sb.ToString().TrimEnd();
    }

    async Task<string> WaitForComposerAsync(FeedWebHost host)
    {
        string probe = "";
        for (int i = 0; i < 12; i++)
        {
            probe = await host.EvalAsync(ComposerProbeScript).ConfigureAwait(false);
            if (ComposerReady(probe)) return probe;
            await Task.Delay(500).ConfigureAwait(false);
        }
        return probe;
    }

    static bool ComposerReady(string probe)
        => probe.Contains("\"ready\":true", StringComparison.Ordinal);

    static string ReadReason(string json)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("reason", out JsonElement reason))
                return reason.GetString() ?? "unknown";
        }
        catch { }
        return "unknown";
    }

    static int ReadCount(string json)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("count", out JsonElement count) && count.TryGetInt32(out int value))
                return value;
        }
        catch { }
        return 0;
    }

    // ─────────────────────────────────────────────────────────────
    // 发布页脚本
    // ─────────────────────────────────────────────────────────────

    const string ComposerProbeScript = """
        (() => {
          const norm = s => (s || '').replace(/\s+/g, ' ').trim();
          const visible = e => {
            const s = getComputedStyle(e);
            const r = e.getBoundingClientRect();
            return s.display !== 'none' && s.visibility !== 'hidden' && r.width > 0 && r.height > 0;
          };
          const docs = [document];
          for (const f of document.querySelectorAll('iframe')) {
            try { if (f.contentDocument) docs.push(f.contentDocument); } catch (e) {}
          }
          const pick = (doc, sels) => {
            for (const sel of sels) {
              for (const e of doc.querySelectorAll(sel)) {
                if (visible(e) && !e.disabled && !e.readOnly) return e;
              }
            }
            return null;
          };
          let title = null, body = null;
          for (const d of docs) {
            title = title || pick(d, ['input[placeholder*="标题"]', 'input[name*="title" i]', 'input[id*="title" i]', 'input[class*="title" i]']);
            body = body || pick(d, ['.ProseMirror', '.ql-editor', '.editor-content', '[contenteditable="true"]', 'body[contenteditable="true"]', 'textarea[placeholder*="正文"]', 'textarea[placeholder*="内容"]', 'textarea[name*="content" i]']);
          }
          const ready = !!title && !!body;
          return JSON.stringify({ ready, title: !!title, body: !!body, iframes: docs.length - 1 });
        })()
        """;

    const string OpenComposerScript = """
        (() => {
          const norm = s => (s || '').replace(/\s+/g, ' ').trim();
          const visible = e => {
            const s = getComputedStyle(e);
            const r = e.getBoundingClientRect();
            return s.display !== 'none' && s.visibility !== 'hidden' && r.width > 0 && r.height > 0;
          };
          const labels = ['发帖', '发贴', '发表新帖', '发布帖子', '写帖子', '发新帖', '发表主题', '发主题帖', '发布', '发表'];
          const labelOf = e => norm(e.innerText) || norm((e.getAttribute('aria-label') || '') + ' ' + (e.getAttribute('title') || ''));
          const cands = [...document.querySelectorAll('button,a,div,span,[role="button"]')]
            .map(e => ({ e, t: labelOf(e) }))
            .filter(x => visible(x.e) && x.t.length > 0 && x.t.length <= 8
              && (labels.includes(x.t) || /发布|发帖|发贴|写帖子/.test(x.t)));
          if (!cands.length) return JSON.stringify({ ok: false, reason: 'no-entry' });
          cands.sort((a, b) => a.t.length - b.t.length);
          const btn = cands[0].e.closest('a,button') || cands[0].e;
          btn.click();
          return JSON.stringify({ ok: true, label: cands[0].t });
        })()
        """;

    const string FillPostScript = """
        (() => {
          const TITLE = __TITLE__, BODY = __BODY__;
          const norm = s => (s || '').replace(/\s+/g, ' ').trim();
          const visible = e => {
            const s = getComputedStyle(e);
            const r = e.getBoundingClientRect();
            return s.display !== 'none' && s.visibility !== 'hidden' && r.width > 0 && r.height > 0;
          };
          const docs = [document];
          for (const f of document.querySelectorAll('iframe')) {
            try { if (f.contentDocument) docs.push(f.contentDocument); } catch (e) {}
          }
          const pick = (sels) => {
            for (const d of docs) {
              for (const sel of sels) {
                for (const e of d.querySelectorAll(sel)) {
                  if (visible(e) && !e.disabled && !e.readOnly) return e;
                }
              }
            }
            return null;
          };
          const titleEl = pick(['input[placeholder*="标题"]', 'input[name*="title" i]', 'input[id*="title" i]', 'input[class*="title" i]']);
          const bodyEl = pick(['.ProseMirror', '.ql-editor', '.editor-content', '[contenteditable="true"]', 'body[contenteditable="true"]', 'textarea[placeholder*="正文"]', 'textarea[placeholder*="内容"]', 'textarea[name*="content" i]']);
          if (!bodyEl) return JSON.stringify({ ok: false, reason: 'no-body' });
          const setVal = (el, text) => {
            if (!el) return false;
            if (el.tagName === 'TEXTAREA' || el.tagName === 'INPUT') {
              const proto = el.tagName === 'TEXTAREA' ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype;
              Object.getOwnPropertyDescriptor(proto, 'value').set.call(el, text);
              el.dispatchEvent(new Event('input', { bubbles: true }));
              el.dispatchEvent(new Event('change', { bubbles: true }));
              return true;
            }
            const doc = el.ownerDocument || document;
            el.focus();
            try { doc.execCommand('selectAll', false, null); } catch (e) {}
            try { doc.execCommand('insertText', false, text); } catch (e) {}
            if (!norm(el.innerText).includes(norm(text).slice(0, 20))) {
              el.innerText = text;
              try { el.dispatchEvent(new InputEvent('input', { bubbles: true, data: text, inputType: 'insertText' })); }
              catch (e) { el.dispatchEvent(new Event('input', { bubbles: true })); }
            }
            return true;
          };
          const titleOk = setVal(titleEl, TITLE);
          const bodyOk = setVal(bodyEl, BODY);
          return JSON.stringify({ ok: bodyOk, title: titleOk, body: bodyOk });
        })()
        """;

    const string AttachImagesScript = """
        (() => {
          const items = __FILES__;
          const dt = new DataTransfer();
          for (const it of items) {
            const bin = atob(it.b64);
            const bytes = new Uint8Array(bin.length);
            for (let i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i);
            dt.items.add(new File([bytes], it.name, { type: it.mime }));
          }
          const inputs = [...document.querySelectorAll('input[type="file"]')];
          const imageInputs = inputs.filter(i => {
            const a = (i.accept || '').toLowerCase();
            return a === '' || a.includes('image') || a.includes('*');
          });
          const input = imageInputs.find(i => i.multiple) || imageInputs[0] || null;
          if (input) {
            try {
              input.files = dt.files;
              input.dispatchEvent(new Event('input', { bubbles: true }));
              input.dispatchEvent(new Event('change', { bubbles: true }));
              return JSON.stringify({ ok: true, mode: 'input', count: input.files.length });
            } catch (e) {}
          }
          const editor = document.querySelector('.ProseMirror, .ql-editor, [contenteditable="true"]');
          if (!editor) return JSON.stringify({ ok: false, reason: 'no-file-input' });
          editor.focus();
          try { editor.dispatchEvent(new DragEvent('drop', { bubbles: true, cancelable: true, dataTransfer: dt })); } catch (e) {}
          try { editor.dispatchEvent(new ClipboardEvent('paste', { bubbles: true, cancelable: true, clipboardData: dt })); } catch (e) {}
          return JSON.stringify({ ok: true, mode: 'drop' });
        })()
        """;

    const string EditorImageCountScript = """
        (() => {
          const blob = [...document.images].filter(i => (i.currentSrc || i.src || '').startsWith('blob:')).length;
          const upload = [...document.querySelectorAll('[class*="upload"] img, [class*="image-item"] img, [class*="image-list"] img, [class*="image-preview"] img')].length;
          const editor = document.querySelector('.ProseMirror, .ql-editor, .editor-content, [contenteditable="true"]');
          let inEditor = 0;
          if (editor) {
            inEditor = [...editor.querySelectorAll('img')].filter(i => {
              const r = i.getBoundingClientRect();
              return r.width >= 60 && r.height >= 60;
            }).length;
          }
          return JSON.stringify({ count: blob + upload + inEditor });
        })()
        """;

    const string PublishClickScript = """
        (() => {
          const norm = s => (s || '').replace(/\s+/g, ' ').trim();
          const visible = e => {
            const s = getComputedStyle(e);
            const r = e.getBoundingClientRect();
            return s.display !== 'none' && s.visibility !== 'hidden' && r.width > 0 && r.height > 0;
          };
          const labels = /^(发布|发表|发帖|发贴|提交)$/;
          const cands = [...document.querySelectorAll('button,a,div,span')]
            .map(e => ({ e, t: norm(e.innerText) }))
            .filter(x => visible(x.e) && labels.test(x.t))
            .filter(x => !x.e.disabled && x.e.getAttribute('aria-disabled') !== 'true');
          if (!cands.length) return JSON.stringify({ ok: false, reason: 'no-submit' });
          cands.sort((a, b) => a.t.length - b.t.length);
          const btn = cands[0].e.closest('button,a') || cands[0].e;
          btn.click();
          return JSON.stringify({ ok: true, label: cands[0].t });
        })()
        """;

    const string VerifyPublishScript = """
        (() => {
          const title = __TITLE__, body = __BODY__;
          const norm = s => (s || '').replace(/\s+/g, ' ').trim();
          let text = norm(document.body ? document.body.innerText : '');
          for (const e of document.querySelectorAll('.ProseMirror, .ql-editor, .editor-content, [contenteditable="true"], textarea')) {
            const t = norm(e.innerText || e.value || '');
            if (t.length > 20) text = text.split(t).join(' ');
          }
          const present = (title.length > 0 && text.includes(norm(title)))
            || (body.length > 0 && text.includes(norm(body).slice(0, 24)));
          const posted = /\/p\/\d+|\/app\/bbs\/link\/\d+/.test(location.href);
          const toast = [...document.querySelectorAll('[class*="toast"],[class*="message"],[class*="notice"]')]
            .map(e => norm(e.innerText)).filter(t => t.length > 0 && t.length < 40).slice(0, 4);
          return JSON.stringify({ present, posted, url: location.href, toast });
        })()
        """;

    const string PostWallScript = """
        (() => {
          const norm = s => (s || '').replace(/\s+/g, ' ').trim();
          const text = norm(document.body ? document.body.innerText : '');
          if (/安全验证|人机验证|请完成验证|拖动滑块|滑动验证/.test(text)) return 'verify';
          if (/扫码登录|请先登录|登录后|登录\/注册|验证码登录/.test(text)) return 'login';
          return 'none';
        })()
        """;
}

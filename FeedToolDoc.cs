using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Alife.Function.FunctionCaller;
using Microsoft.Extensions.Logging;

namespace presents.FeedScroll;

/// <summary>
/// 工具文档注入：框架默认会在 AI 调隐式标签时把整份文档 Poke 进对话。
/// 这里把它替换成可配置行为：完整注入按天限次；关闭后只回一行函数名，减少重复上下文。
/// </summary>
public partial class FeedScrollModule
{
    XmlHandler? feedHandler;
    bool toolDocTriggerHooked;

    void CustomizeImplicitToolDocument(XmlFunctionCaller functionCaller, XmlHandler handler)
    {
        try
        {
            string triggerName = handler.Name + "_Trigger";
            XmlHandler? trigger = functionCaller.HandlerTable.GetAllHandlers()
                .FirstOrDefault(h => string.Equals(h.Name, triggerName, StringComparison.OrdinalIgnoreCase));
            XmlFunction? function = trigger?.Functions
                .FirstOrDefault(f => string.Equals(f.Name, handler.Name, StringComparison.OrdinalIgnoreCase));

            // 兜底：实际注册名可能带命名空间/大小写差异，按「函数名 + _Trigger 处理器」再扫一遍。
            if (function == null)
            {
                foreach (XmlHandler candidate in functionCaller.HandlerTable.GetAllHandlers())
                {
                    if (!candidate.Name.EndsWith("_Trigger", StringComparison.OrdinalIgnoreCase)) continue;
                    XmlFunction? match = candidate.Functions.FirstOrDefault(
                        f => string.Equals(f.Name, handler.Name, StringComparison.OrdinalIgnoreCase));
                    if (match != null) { function = match; triggerName = candidate.Name; break; }
                }
            }

            if (function == null)
            {
                logger.LogWarning("贴吧/小黑盒插件：没找到隐式文档触发器 {Trigger}，保留框架默认注入行为", triggerName);
                return;
            }

            logger.LogInformation("贴吧/小黑盒插件：已接管隐式文档触发器 {Trigger}，完整文档注入={Inject}",
                triggerName, Configuration.InjectToolDocument);
            toolDocTriggerHooked = true;

            function.Invoker = (_, _) =>
            {
                EnsureToolDocDay();
                if (!Configuration.InjectToolDocument)
                {
                    // 关闭后只给一行函数名，绝不展开整份工具文档；旧对话里的文档块需要新开对话才会消失。
                    interactor.Poke(ToolDocBrief(handler));
                    return Task.CompletedTask;
                }

                int limit = Math.Clamp(Configuration.ToolDocInjectDailyLimit, 0, 24);
                if (limit > 0 && state.ToolDocInjectToday >= limit)
                {
                    interactor.Poke(ToolDocBrief(handler)
                                    + $"（今日完整文档已注入 {limit} 次，够用了；控制台可改上限或关闭）");
                    return Task.CompletedTask;
                }

                interactor.Poke(BuildToolDocument(handler));
                state.ToolDocInjectToday++;
                state.ToolDocDayKey = DateTime.Now.ToString("yyyy-MM-dd");
                SaveState();
                return Task.CompletedTask;
            };
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "贴吧/小黑盒插件：自定义工具文档注入失败");
        }
    }

    void EnsureToolDocDay()
    {
        string today = DateTime.Now.ToString("yyyy-MM-dd");
        if (state.ToolDocDayKey != today)
        {
            state.ToolDocDayKey = today;
            state.ToolDocInjectToday = 0;
        }
    }

    string BuildToolDocument(XmlHandler handler)
    {
        string description = string.IsNullOrWhiteSpace(Configuration.ToolPromptDescription)
            ? DefaultToolDescription
            : Configuration.ToolPromptDescription.Trim();
        string explanation = string.IsNullOrWhiteSpace(Configuration.ToolPromptExplanation)
            ? DefaultToolExplanation
            : Configuration.ToolPromptExplanation.Trim();

        var sb = new StringBuilder();
        sb.AppendLine("#### 刷贴吧与小黑盒");
        sb.AppendLine(description);
        sb.AppendLine();
        sb.AppendLine("#### 提供函数");
        sb.AppendLine(handler.FunctionDocument());
        if (explanation.Length > 0)
        {
            sb.AppendLine();
            sb.AppendLine("#### 详细说明");
            sb.AppendLine("```");
            sb.AppendLine(explanation);
            sb.AppendLine("```");
        }
        return sb.ToString().TrimEnd();
    }

    static string ToolDocBrief(XmlHandler handler)
    {
        string names = string.Join("、", handler.Functions.Select(f => f.Name));
        return "[刷贴吧与小黑盒] 可用函数：" + names + "。参数按之前学过的用法；完整文档今日不再重复。";
    }

    public string UiToolDocStatus()
    {
        EnsureToolDocDay();
        int limit = Math.Clamp(Configuration.ToolDocInjectDailyLimit, 0, 24);
        return $"今日完整注入 {state.ToolDocInjectToday}/{(limit <= 0 ? "不限" : limit.ToString())} 次 · "
               + $"{(Configuration.InjectToolDocument ? "注入开" : "注入关")}"
               + $" · 触发器接管：{(toolDocTriggerHooked ? "是" : "否（可能仍走框架默认整份文档）")}";
    }

    public string UiToggleToolDocument()
    {
        Configuration.InjectToolDocument = !Configuration.InjectToolDocument;
        PersistConfiguration();
        return Configuration.InjectToolDocument
            ? "工具文档注入已开：调用时会展开完整文档，受每日次数限制。"
            : "工具文档注入已关：之后只回一行函数名提醒。注意：已经注入到当前对话里的整份文档不会被撤回，需要新开对话或清空上下文才会消失。";
    }

    public string UiSaveToolDocument(string description, string explanation, string dailyLimit)
    {
        Configuration.ToolPromptDescription = (description ?? "").Trim();
        Configuration.ToolPromptExplanation = (explanation ?? "").Trim();
        Configuration.ToolDocInjectDailyLimit = Math.Clamp(ToInt(dailyLimit, 2), 0, 24);
        PersistConfiguration();
        string limitText = Configuration.ToolDocInjectDailyLimit <= 0
            ? "不限"
            : Configuration.ToolDocInjectDailyLimit + " 次";
        return $"工具文档已保存（每天最多完整注入 {limitText}）。短描述要重载插件后才会更新到系统提示词。";
    }

    public string UiToolDocumentPreview()
        => feedHandler == null ? "模块未激活" : BuildToolDocument(feedHandler);

    public string UiResetToolDocCounter()
    {
        state.ToolDocInjectToday = 0;
        state.ToolDocDayKey = DateTime.Now.ToString("yyyy-MM-dd");
        SaveState();
        return "工具文档今日注入次数已清零，下次调用会给完整文档。";
    }
}

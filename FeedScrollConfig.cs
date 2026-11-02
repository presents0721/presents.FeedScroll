using System;
using System.ComponentModel;

namespace presents.FeedScroll;

public class FeedScrollConfig
{
    [DisplayName("启用贴吧/小黑盒抓取")]
    [Description("关闭后不再打开网页，所有工具都返回「已停用」。")]
    public bool Enable { get; set; } = true;

    [DisplayName("列表条数上限")]
    [Description("一次返回多少条帖子，默认 20。太多会占 token。")]
    public int MaxListItems { get; set; } = 20;

    [DisplayName("正文上限（字）")]
    [Description("打开帖子时正文最多返回多少字，默认 2500。")]
    public int MaxPostChars { get; set; } = 2500;

    [DisplayName("顶层评论条数")]
    [Description("打开帖子时最多带几条顶层评论，默认 6。0 = 不读评论区。太多会占 token。")]
    public int MaxReplies { get; set; } = 6;

    [DisplayName("楼中楼条数")]
    [Description("打开帖子时最多带几条楼中楼内层回复（全部评论合计），默认 3。0 = 不读楼中楼。")]
    public int MaxNestedReplies { get; set; } = 3;

    [DisplayName("单条评论字数上限")]
    [Description("每条评论最多返回多少字，默认 160。")]
    public int MaxCommentChars { get; set; } = 160;

    [DisplayName("导航最小间隔（秒）")]
    [Description("两次打开网页之间至少隔多久，默认 3 秒，避免被站点当爬虫。")]
    public int MinIntervalSeconds { get; set; } = 3;

    [DisplayName("页面稳定等待（毫秒）")]
    [Description("导航完成后等页面渲染多久再抽取，默认 1200 毫秒。")]
    public int SettleMilliseconds { get; set; } = 1200;

    [DisplayName("允许抓取的站点")]
    [Description("逗号分隔的域名白名单，默认贴吧与小黑盒；FeedReadUrl 只允许这些站点。")]
    public string AllowedHosts { get; set; } = "tieba.baidu.com,xiaoheihe.cn";

    [DisplayName("只读备注")]
    [Description("仅提示用：默认以读为主；发帖需另开「允许发新帖」并关闭「发帖演练」。收藏/保存图片/评论的授权见下面开关。")]
    public bool ReadOnly { get; set; } = true;

    // ─────────────────────────────────────────────────────────────
    // 写操作（收藏 / 保存图片 / 评论回复）
    // ─────────────────────────────────────────────────────────────

    [DisplayName("允许写操作")]
    [Description("写操作总开关，默认开（只覆盖收藏与保存图片；评论/回复另受演练模式约束）。")]
    public bool EnableWriteActions { get; set; } = true;

    [DisplayName("允许收藏帖子")]
    [Description("登录后可收藏/取消收藏帖子。默认开。")]
    public bool AllowFavorite { get; set; } = true;

    [DisplayName("收藏每日上限")]
    [Description("每天最多执行几次收藏/取消收藏，默认 20。")]
    public int FavoriteDailyLimit { get; set; } = 20;

    [DisplayName("允许保存帖子图片")]
    [Description("把帖子里的图片下载到本地 PluginData/presents.FeedScroll/images/。默认开；这不是账号收藏。")]
    public bool AllowSaveImages { get; set; } = true;

    [DisplayName("单次保存图片上限")]
    [Description("一次最多下载几张图，默认 8。")]
    public int SaveImagesMax { get; set; } = 8;

    [DisplayName("允许评论/回复")]
    [Description("是否允许评论与回复。默认开，但受下面的演练模式约束。")]
    public bool AllowComment { get; set; } = true;

    [DisplayName("评论演练模式")]
    [Description("默认开：只返回将要发布的内容与目标，不真正发送；关闭且主人明确确认后才发送。")]
    public bool CommentDryRun { get; set; } = true;

    [DisplayName("评论每日上限")]
    [Description("每天最多真发几条评论/回复，默认 3。演练不计入。")]
    public int CommentDailyLimit { get; set; } = 3;

    [DisplayName("写操作最小间隔（秒）")]
    [Description("两次写操作之间至少隔多久，默认 5 秒。")]
    public int ActionIntervalSeconds { get; set; } = 5;

    // ─────────────────────────────────────────────────────────────
    // 发新帖（默认演练；附图只从插件 images 目录取）
    // ─────────────────────────────────────────────────────────────

    [DisplayName("允许发新帖")]
    [Description("发帖总开关，默认开；但只有关闭下面的「发帖演练模式」且主人明确确认才会真发。")]
    public bool AllowPost { get; set; } = true;

    [DisplayName("发帖演练模式")]
    [Description("默认开：只返回标题/正文/附图预览，不打开发布页。关闭且主人明确说“发”后才执行。")]
    public bool PostDryRun { get; set; } = true;

    [DisplayName("发帖每日上限")]
    [Description("每天最多真发几帖，默认 1。演练不计入。")]
    public int PostDailyLimit { get; set; } = 1;

    [DisplayName("帖子标题上限（字）")]
    [Description("默认 60 字；贴吧标题必填，小黑盒可空。")]
    public int PostTitleMaxChars { get; set; } = 60;

    [DisplayName("帖子正文上限（字）")]
    [Description("默认 2000 字。")]
    public int PostMaxChars { get; set; } = 2000;

    [DisplayName("单帖附图上限")]
    [Description("默认 4 张；0 = 不附图。只从 PluginData/presents.FeedScroll/images 目录取。")]
    public int PostMaxImages { get; set; } = 4;

    [DisplayName("单张图片大小上限（MB）")]
    [Description("默认 5MB；太大的图先压缩或截小再发。")]
    public int PostImageMaxMB { get; set; } = 5;

    [DisplayName("贴吧发帖入口")]
    [Description("默认打开吧页面再点「发贴」；也可填直接发帖页，{forum} 会替换成吧名。")]
    public string PostComposerTieba { get; set; } = "https://tieba.baidu.com/f?kw={forum}";

    [DisplayName("小黑盒发帖入口")]
    [Description("默认打开社区首页再找「发布」；知道直接发布页的话填在这里。")]
    public string PostComposerXhh { get; set; } = "https://www.xiaoheihe.cn/app/bbs/home";

    // ─────────────────────────────────────────────────────────────
    // 工具文档注入（控制每次注入的上下文长度）
    // ─────────────────────────────────────────────────────────────

    [DisplayName("注入完整工具文档")]
    [Description("开：AI 调 <FeedScrollModule/> 时按下面内容注入完整文档；关：只回一行函数名提醒，不再展开长文。")]
    public bool InjectToolDocument { get; set; } = false;

    [DisplayName("工具文档每天最多完整注入几次")]
    [Description("默认 2 次；到上限后只回简短提醒。0 = 每次都完整注入。")]
    public int ToolDocInjectDailyLimit { get; set; } = 2;

    [DisplayName("工具文档短描述")]
    [Description("系统提示词里那一行简介；改完重载插件生效。")]
    public string ToolPromptDescription { get; set; } = FeedScrollModule.DefaultToolDescription;

    [DisplayName("工具文档详细说明")]
    [Description("AI 调 <FeedScrollModule/> 时注入的规则文本；函数签名由代码自动生成。控制台可直接编辑并预览。")]
    public string ToolPromptExplanation { get; set; } = FeedScrollModule.DefaultToolExplanation;

    // ─────────────────────────────────────────────────────────────
    // 内容屏蔽（贴吧/标签/关键词）
    // ─────────────────────────────────────────────────────────────

    [DisplayName("屏蔽的贴吧")]
    [Description("逗号或换行分隔。命中的吧不会刷（列表/搜索直接拒绝）。默认「核战避难所」，可按需增删。")]
    public string BlockedForums { get; set; } = "核战避难所";

    [DisplayName("屏蔽的标签/关键词")]
    [Description("逗号或换行分隔。标题、摘要、正文命中任一关键词就不再返回（列表先过滤，正文再兜底）。默认屏蔽常见 NSFW/性相关内容。")]
    public string BlockedKeywords { get; set; } =
        "nsfw,福利姬,裸照,约炮,色情,成人内容,里番,本子,性压抑";

    [DisplayName("屏蔽大小写不敏感")]
    [Description("关键词匹配默认不区分大小写；关掉则严格区分。")]
    public bool BlockCaseInsensitive { get; set; } = true;

    // ─────────────────────────────────────────────────────────────
    // 自主浏览（角色按自己的兴趣决定看什么）
    // ─────────────────────────────────────────────────────────────

    [DisplayName("允许自主浏览")]
    [Description("开：角色可以不等主人指派，用 FeedWander 按自己的兴趣去刷/搜。关：只能由主人明确指定吧名或关键词。")]
    public bool EnableAutonomy { get; set; } = true;

    [DisplayName("自主浏览每日上限")]
    [Description("角色一天最多自己刷几次，默认 6。到上限后要主人开口才继续，避免悄悄烧 token。")]
    public int AutonomyDailyLimit { get; set; } = 6;

    [DisplayName("自主浏览自动记笔记")]
    [Description("开启后，自主浏览完会把这一轮看到的帖子写进 notes/reading-notes.md；是否同时注入聊天历史由下面两项控制。")]
    public bool AutoSaveReadingNotes { get; set; } = true;

    [DisplayName("阅读笔记注入聊天")]
    [Description("开：把本轮笔记按下面的模板注入聊天历史，供 Nexus.Memory 收成 L3；关：只写笔记文件，不打扰对话。")]
    public bool InjectReadingNote { get; set; } = false;

    [DisplayName("每天最多注入几条")]
    [Description("默认 2 条。到上限后只写笔记文件，不再每轮都占用上下文；0 = 不限制。")]
    public int ReadingNoteInjectDailyLimit { get; set; } = 2;

    [DisplayName("阅读笔记注入模板")]
    [Description("可用占位符：{date} {site} {topic} {reason} {count} {titles} {feeling}。控制台里可直接编辑并保存。")]
    public string ReadingNoteTemplate { get; set; } =
        "[我的社区阅读笔记] {date} · {site} · {topic}\n这轮翻到 {count} 条：{titles}\n{feeling}";

    [DisplayName("启用定时自主浏览")]
    [Description("开：到了下面时段且当天轮数未满，插件自己刷一轮，不用主人喊；关：只在主动调用 FeedWander 时刷。")]
    public bool EnableScheduledBrowse { get; set; }

    [DisplayName("定时时段")]
    [Description("一行一个，格式 09:00-10:30，可加任意多个；跨午夜可写 22:00-01:00。控制台可增删。")]
    public string BrowseSlots { get; set; } = "09:00-10:00\n14:00-15:00\n21:00-22:00";

    [DisplayName("每天自动刷几轮")]
    [Description("默认 3 轮；到点且未满轮数才刷。0 = 不自动刷。也受「自主浏览每日上限」约束。")]
    public int BrowseRoundsPerDay { get; set; } = 3;

    [DisplayName("每轮刷几条帖子")]
    [Description("默认 5 条；这一轮只看前几条，不改变全局列表上限。")]
    public int BrowsePostsPerRound { get; set; } = 5;

    [DisplayName("两轮最小间隔（分钟）")]
    [Description("默认 60 分钟；避免在时段开头把当天轮数一口气刷完。")]
    public int BrowseRoundGapMinutes { get; set; } = 60;

    [DisplayName("种子兴趣")]
    [Description("角色还没自己写兴趣时，从这些话题里挑。逗号分隔，默认偏日常向。")]
    public string SeedInterests { get; set; } = "日常,生活,摄影,猫,游戏杂谈,数码,编程";

    [DisplayName("正文缓存保留天数")]
    [Description("默认 7 天；articles 里的全文缓存超期自动清理。")]
    public int ArticleRetentionDays { get; set; } = 7;

    [DisplayName("正文缓存最多保留篇数")]
    [Description("默认 50 篇；超过按时间删旧。")]
    public int ArticleMaxFiles { get; set; } = 50;
}

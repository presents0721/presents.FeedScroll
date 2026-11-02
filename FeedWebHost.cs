using System;
using System.Drawing;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace presents.FeedScroll;

/// <summary>
/// 插件自己的 WebView2 浏览器：独立 user data 目录，登录 Cookie 会留在里面。
/// 窗口默认最小化在托盘；需要登录时从控制台点「打开浏览器窗口」。
/// </summary>
public sealed class FeedWebHost : IAsyncDisposable
{
    volatile FeedBrowserForm? form;
    readonly Thread thread;
    bool disposed;

    public string UserDataFolder { get; }
    public bool IsReady => form?.SafeReady ?? false;
    public string CurrentUrl => form?.SafeUrl ?? "";

    public FeedWebHost(string userDataFolder)
    {
        UserDataFolder = userDataFolder;
        // 窗体和 WebView2 控件必须在自己的 STA 线程里创建：
        // 在调用方线程 new 出来再换线程 Application.Run，会触发
        // 「CoreWebView2Controller members can only be accessed from the UI thread」。
        thread = new Thread(() =>
        {
            try
            {
                var created = new FeedBrowserForm(userDataFolder);
                form = created;
                Application.Run(created);
            }
            catch (Exception e)
            {
                form?.Fail(e);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
    }

    public async Task<bool> WaitReadyAsync(TimeSpan timeout)
    {
        DateTime deadline = DateTime.Now + timeout;
        while (DateTime.Now < deadline)
        {
            FeedBrowserForm? current = form;
            if (current != null && current.SafeReady) return true;
            if (current != null && current.SafeFailed) return false;
            await Task.Delay(120).ConfigureAwait(false);
        }
        return false;
    }

    public Task<FeedListResult> NavigateAsync(string url, int timeoutSeconds = 20)
    {
        FeedBrowserForm? current = form;
        if (current == null)
            return Task.FromResult(new FeedListResult { Success = false, Url = url, Error = "浏览器还没就绪" });
        return current.RunUiAsync(async core =>
        {
            var tcs = new TaskCompletionSource<bool>();
            void Handler(object? sender, CoreWebView2NavigationCompletedEventArgs e)
            {
                core.NavigationCompleted -= Handler;
                tcs.TrySetResult(e.IsSuccess);
            }
            core.NavigationCompleted += Handler;
            try { core.Navigate(url); }
            catch (Exception e)
            {
                core.NavigationCompleted -= Handler;
                return new FeedListResult { Success = false, Url = url, Error = e.Message };
            }
            Task completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(Math.Max(5, timeoutSeconds)))).ConfigureAwait(false);
            if (completed != tcs.Task)
            {
                core.NavigationCompleted -= Handler;
                return new FeedListResult { Success = false, TimedOut = true, Url = url, Error = "页面加载超时" };
            }
            return new FeedListResult { Success = tcs.Task.Result, Url = current.SafeUrl.Length > 0 ? current.SafeUrl : url };
        });
    }

    public async Task<string> EvalAsync(string script)
    {
        FeedBrowserForm? currentForm = form;
        if (currentForm == null) return "";
        string raw = await currentForm.RunUiAsync(core => core.ExecuteScriptAsync(script)).ConfigureAwait(false);
        if (raw.Length > 0 && raw[0] == '"')
        {
            try
            {
                string? decoded = JsonSerializer.Deserialize<string>(raw);
                if (decoded != null) return decoded;
            }
            catch
            {
                // 不是 JSON 字符串就原样返回
            }
        }
        return raw;
    }

    public void ShowWindow() => form?.ShowFromTray();

    /// <summary>让窗口导航到指定页面（贴吧首页 / 小黑盒社区）。</summary>
    public void NavigateTo(string url) => form?.GoTo(url);

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        try { form?.CloseFromHost(); } catch { }
        try
        {
            if (thread.IsAlive) thread.Join(TimeSpan.FromSeconds(3));
        }
        catch { }
        await Task.CompletedTask;
    }
}

sealed class FeedBrowserForm : Form
{
    readonly string userDataFolder;
    readonly WebView2 webView;
    readonly TextBox address;
    readonly NotifyIcon tray;
    CoreWebView2? core;
    volatile string safeUrl = "";

    public bool SafeReady { get; private set; }
    public bool SafeFailed { get; private set; }
    public string SafeUrl => safeUrl;

    bool closing;

    public FeedBrowserForm(string userDataFolder)
    {
        this.userDataFolder = userDataFolder;
        Text = "Alife · 贴吧/小黑盒浏览器";
        Size = new Size(1100, 780);
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = false;
        WindowState = FormWindowState.Minimized;

        var bar = new Panel { Dock = DockStyle.Top, Height = 34 };
        address = new TextBox { Dock = DockStyle.Fill, Font = new Font("Microsoft YaHei UI", 9f) };
        var go = new Button { Text = "前往", Dock = DockStyle.Right, Width = 64 };
        var back = new Button { Text = "后退", Dock = DockStyle.Right, Width = 64 };
        var xhh = new Button { Text = "小黑盒", Dock = DockStyle.Right, Width = 64 };
        var tieba = new Button { Text = "贴吧", Dock = DockStyle.Right, Width = 52 };
        var hint = new Label
        {
            Text = "小黑盒登录：点顶部「小黑盒」进社区，扫码/登录一次，Cookie 会留在本插件目录。贴吧点「贴吧」回到首页。",
            Dock = DockStyle.Bottom,
            Height = 26,
            ForeColor = Color.DimGray
        };
        bar.Controls.Add(address);
        bar.Controls.Add(go);
        bar.Controls.Add(back);
        bar.Controls.Add(xhh);
        bar.Controls.Add(tieba);
        Controls.Add(webView = new WebView2 { Dock = DockStyle.Fill });
        Controls.Add(hint);
        Controls.Add(bar);

        go.Click += (_, _) => NavigateFromBar();
        back.Click += (_, _) =>
        {
            if (core != null && core.CanGoBack) core.GoBack();
        };
        tieba.Click += (_, _) => GoTo(TiebaHome);
        xhh.Click += (_, _) => GoTo(XhhHome);
        address.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.Handled = true;
            NavigateFromBar();
        };

        tray = new NotifyIcon
        {
            Text = "Alife · 贴吧/小黑盒浏览器",
            Icon = SystemIcons.Application,
            Visible = true
        };
        tray.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) ShowFromTray();
        };
        tray.DoubleClick += (_, _) => ShowFromTray();
    }

    void NavigateFromBar()
    {
        string text = address.Text.Trim();
        if (text.Length == 0 || core == null) return;
        if (!text.StartsWith("http", StringComparison.OrdinalIgnoreCase)) text = "https://" + text;
        try { core.Navigate(text); } catch { }
    }

    public const string TiebaHome = "https://tieba.baidu.com/";
    public const string XhhHome = "https://www.xiaoheihe.cn/app/bbs/home";

    /// <summary>从任意线程切换到这个窗口所在的 UI 线程再导航。</summary>
    public void GoTo(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        try
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => GoToOnUi(url)));
                return;
            }
            GoToOnUi(url);
        }
        catch { }
    }

    void GoToOnUi(string url)
    {
        address.Text = url;
        try { core?.Navigate(url); } catch { }
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        _ = InitWebViewAsync();
    }

    /// <summary>
    /// WebView2 的 Environment 可以在后台线程建；但 EnsureCoreWebView2 / CoreWebView2 的成员
    /// 只能在创建控件的 UI 线程碰，所以每一步都显式切回 UI 线程。
    /// </summary>
    async Task InitWebViewAsync()
    {
        try
        {
            CoreWebView2Environment env = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null, userDataFolder: userDataFolder, options: null).ConfigureAwait(false);
            if (IsDisposed) return;
            try { BeginInvoke(new Action(() => BeginEnsureCore(env))); } catch { }
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
    }

    void BeginEnsureCore(CoreWebView2Environment env)
    {
        if (IsDisposed) return;
        try
        {
            webView.EnsureCoreWebView2Async(env).ContinueWith(t =>
            {
                if (t.IsFaulted)
                {
                    Exception ex = t.Exception?.GetBaseException() ?? new Exception("WebView2 初始化失败");
                    Fail(ex);
                    return;
                }
                try { BeginInvoke(new Action(FinishEnsureCore)); } catch { }
            }, TaskScheduler.Default);
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
    }

    void FinishEnsureCore()
    {
        if (IsDisposed) return;
        try
        {
            core = webView.CoreWebView2;
            safeUrl = core.Source ?? "";
            core.Settings.UserAgent =
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36 Edge/122.0.0.0";
            core.SourceChanged += (_, _) =>
            {
                try
                {
                    safeUrl = core.Source ?? "";
                    if (!address.Focused) address.Text = safeUrl;
                }
                catch { }
            };
            // 首次打开别停在 about:blank：直接落到贴吧首页，登录/验证都在这里做
            try { GoToOnUi(TiebaHome); } catch { }
            SafeReady = true;
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
    }

    public void Fail(Exception ex)
    {
        SafeFailed = true;
        try
        {
            if (IsDisposed) return;
            if (IsHandleCreated && InvokeRequired)
            {
                BeginInvoke(new Action(() => ShowFail(ex)));
                return;
            }
            ShowFail(ex);
        }
        catch { }
    }

    void ShowFail(Exception ex)
    {
        try
        {
            MessageBox.Show("贴吧/小黑盒浏览器初始化失败：\n" + ex.Message, "presents.FeedScroll",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch { }
    }

    public Task<T> RunUiAsync<T>(Func<CoreWebView2, Task<T>> action)
    {
        var tcs = new TaskCompletionSource<T>();
        if (IsDisposed || !SafeReady || core == null)
        {
            tcs.TrySetException(new InvalidOperationException("浏览器还没就绪"));
            return tcs.Task;
        }
        try
        {
            BeginInvoke(new Action(async () =>
            {
                try
                {
                    CoreWebView2? current = core;
                    if (current == null) throw new InvalidOperationException("浏览器已关闭");
                    tcs.TrySetResult(await action(current));
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            }));
        }
        catch (Exception ex)
        {
            tcs.TrySetException(ex);
        }
        return tcs.Task;
    }

    public void ShowFromTray()
    {
        try
        {
            BeginInvoke(new Action(() =>
            {
                Show();
                ShowInTaskbar = true;
                WindowState = FormWindowState.Normal;
                Activate();
            }));
        }
        catch { }
    }

    public void CloseFromHost()
    {
        closing = true;
        try
        {
            BeginInvoke(new Action(() =>
            {
                tray.Visible = false;
                Close();
            }));
        }
        catch { }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!closing && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            ShowInTaskbar = false;
            WindowState = FormWindowState.Minimized;
            return;
        }
        tray.Visible = false;
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            tray.Dispose();
            webView.Dispose();
        }
        base.Dispose(disposing);
    }
}

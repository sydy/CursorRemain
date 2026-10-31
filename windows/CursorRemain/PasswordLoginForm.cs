using CursorTokenCore;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace CursorRemain;

sealed class PasswordLoginForm : Form
{
    readonly WebView2 _web = new() { Dock = DockStyle.Fill };
    readonly Label _status = new()
    {
        AutoSize = true,
        Text = "正在打开官方登录页…",
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        Padding = new Padding(8, 4, 8, 4),
    };
    readonly string _email;
    readonly string _password;
    readonly string _profileId;
    readonly string _startUrl;
    readonly string _pollUuid;
    readonly string _pollVerifier;
    readonly System.Windows.Forms.Timer _poll = new() { Interval = 1500 };
    DateTime _deadline;
    DateTime _nextPoll = DateTime.MinValue;
    bool _done;
    bool _polling;

    public string Token { get; private set; } = "";

    public PasswordLoginForm(string email, string password, string? profileId = null, string? startUrl = null, string? pollUuid = null, string? pollVerifier = null)
    {
        _email = email;
        _password = password;
        _profileId = CursorPasswordLogin.ValidProfileId(profileId);
        if (_profileId.Length == 0) _profileId = Guid.NewGuid().ToString("D");
        _startUrl = AllowedLoginUrl(startUrl);
        _pollUuid = pollUuid ?? "";
        _pollVerifier = pollVerifier ?? "";
        Text = "登录 Cursor 账号";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(800, 600);
        ClientSize = new Size(1000, 750);
        var icon = AppWindow.CreateIcon();
        if (icon is not null) Icon = icon;
        var cancel = UiChrome.Button("取消");
        cancel.DialogResult = DialogResult.Cancel;
        var bar = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            Padding = new Padding(12, 8, 12, 8),
        };
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.Controls.Add(_status, 0, 0);
        bar.Controls.Add(cancel, 1, 0);
        Controls.Add(_web);
        Controls.Add(bar);
        bar.BringToFront();
        CancelButton = cancel;
        UiChrome.Apply(this);
        _poll.Tick += async (_, _) => await TickAsync();
        Load += async (_, _) => await StartAsync();
        FormClosed += (_, _) => _poll.Stop();
    }

    async Task StartAsync()
    {
        try
        {
            var data = LoginProfileStore.DirectoryFor(_profileId);
            var env = await CoreWebView2Environment.CreateAsync(null, data);
            await _web.EnsureCoreWebView2Async(env);
            _web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            _web.CoreWebView2.NavigationCompleted += async (_, _) =>
            {
                await InjectAsync();
                await TryCaptureAsync();
            };
            _deadline = DateTime.UtcNow.AddSeconds(CursorPasswordLogin.TimeoutSeconds);
            _poll.Start();
            _status.Text = string.IsNullOrEmpty(_password)
                ? "请在此账号的独立环境中完成登录…"
                : "正在自动填写，验证码请在窗口里完成…";
            _web.CoreWebView2.Navigate(_startUrl);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            _status.Text = "本机未安装 WebView2 运行时，请先安装或改用手动粘贴 Token。";
        }
        catch (Exception ex)
        {
            _status.Text = "无法打开登录页：" + ex.Message;
        }
    }

    async Task TickAsync()
    {
        if (_done) return;
        if (DateTime.UtcNow >= _deadline)
        {
            Finish(false, "等待登录超时，请完成验证码后重试，或改用手动粘贴。");
            return;
        }
        await InjectAsync();
        await TryCaptureAsync();
        await TryPollAsync();
    }

    async Task TryPollAsync()
    {
        if (_done || _polling || _pollUuid.Length == 0 || _pollVerifier.Length == 0 || DateTime.UtcNow < _nextPoll) return;
        _polling = true;
        _nextPoll = DateTime.UtcNow.AddSeconds(3);
        try
        {
            var result = await CursorPasswordLogin.PollSession(_pollUuid, _pollVerifier);
            if (_done || string.IsNullOrWhiteSpace(result.Token)) return;
            Token = result.Token;
            Finish(true, "已登录");
        }
        finally { _polling = false; }
    }

    async Task InjectAsync()
    {
        if (_done || _web.CoreWebView2 is null || string.IsNullOrEmpty(_password)) return;
        try
        {
            var state = await _web.CoreWebView2.ExecuteScriptAsync(CursorPasswordLogin.AutofillScript(_email, _password));
            if (state.Contains("need-user", StringComparison.OrdinalIgnoreCase))
                _status.Text = "请在窗口里完成验证码或邮箱验证码…";
        }
        catch
        {
            // page may still be loading
        }
    }

    async Task TryCaptureAsync()
    {
        if (_done || _web.CoreWebView2 is null) return;
        try
        {
            var cookies = new List<(string, string)>();
            foreach (var host in new[] { "https://cursor.com", "https://www.cursor.com" })
            {
                foreach (var c in await _web.CoreWebView2.CookieManager.GetCookiesAsync(host))
                    cookies.Add((c.Name, c.Value));
            }
            var token = CursorPasswordLogin.TokenFromCookies(cookies);
            if (string.IsNullOrWhiteSpace(token)) return;
            Token = token;
            Finish(true, "已获取 Token");
        }
        catch
        {
            // keep waiting
        }
    }

    static string AllowedLoginUrl(string? raw)
    {
        if (Uri.TryCreate(raw, UriKind.Absolute, out var uri)
            && uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase)
            && (uri.Host.Equals("cursor.com", StringComparison.OrdinalIgnoreCase)
                || uri.Host.Equals("www.cursor.com", StringComparison.OrdinalIgnoreCase)))
            return uri.AbsoluteUri;
        return CursorPasswordLogin.LoginUrl;
    }

    void Finish(bool ok, string message)
    {
        if (_done) return;
        _done = true;
        _poll.Stop();
        _status.Text = message;
        DialogResult = ok ? DialogResult.OK : DialogResult.Cancel;
        if (!IsDisposed) Close();
    }
}

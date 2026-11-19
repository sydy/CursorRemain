using CursorTokenCore;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace CursorRemain;

/// <summary>
/// Doubao login window. Each account id gets its own WebView2 user-data folder so
/// two Doubao accounts never share a session; the login result is the full Cookie
/// header, which the caller stores encrypted.
/// </summary>
sealed class DoubaoLoginForm : Form
{
    readonly WebView2 _web = new() { Dock = DockStyle.Fill };
    readonly Label _status = new()
    {
        AutoSize = true,
        Text = "正在打开豆包登录页…",
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        Padding = new Padding(8, 4, 8, 4),
    };
    readonly string _profileId;
    readonly System.Windows.Forms.Timer _poll = new() { Interval = 1500 };
    DateTime _deadline;
    bool _done;

    /// <summary>Full Doubao Cookie header, only set when <see cref="LooksLoggedIn"/> passes.</summary>
    public string Cookie { get; private set; } = "";

    public DoubaoLoginForm(string? accountId = null)
    {
        var id = DoubaoCredentials.IsValidAccountId(accountId) ? accountId! : DoubaoCredentials.RandomAccountId();
        _profileId = DoubaoCredentials.ProfileUuid(id);
        Text = "登录豆包账号";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(820, 620);
        ClientSize = new Size(1020, 760);
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
            _web.CoreWebView2.NavigationCompleted += async (_, _) => await TryCaptureAsync();
            _deadline = DateTime.UtcNow.AddSeconds(180);
            _poll.Start();
            _status.Text = "请在这个账号自己的窗口里完成登录…";
            _web.CoreWebView2.Navigate(DoubaoCredentials.LoginUrl);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            _status.Text = "本机未安装 WebView2 运行时，请安装后重试，或改用手动粘贴 Cookie。";
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
            Finish(false, "等待登录超时，请重试或改用手动粘贴 Cookie。");
            return;
        }
        await TryCaptureAsync();
    }

    async Task TryCaptureAsync()
    {
        if (_done || _web.CoreWebView2 is null) return;
        try
        {
            var pairs = new List<(string Name, string Value)>();
            foreach (var host in new[] { "https://www.doubao.com", "https://doubao.com" })
            {
                foreach (var c in await _web.CoreWebView2.CookieManager.GetCookiesAsync(host))
                    pairs.Add((c.Name, c.Value));
            }
            if (pairs.Count == 0) return;
            var cookie = DoubaoCredentials.Normalize(string.Join("; ", pairs.Select(p => p.Name + "=" + p.Value)));
            if (!DoubaoCredentials.LooksLoggedIn(cookie)) return;
            Cookie = cookie;
            Finish(true, "已获取豆包 Cookie");
        }
        catch
        {
            // keep waiting
        }
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

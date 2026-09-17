using CursorTokenCore;

namespace CursorRemain;

static class CursorLoginUi
{
    public static async Task<CursorAuthApplyResult> Run(Account? acc, IWin32Window? owner)
    {
        if (acc is null)
            return new CursorAuthApplyResult(false, "请先选择账号");
        if (acc.TokenDecryptFailed || string.IsNullOrWhiteSpace(acc.Token))
            return new CursorAuthApplyResult(false, "当前账号没有可用 Token");
        var values = CursorAuth.BuildValues(
            acc.Token,
            email: !string.IsNullOrWhiteSpace(acc.Email) ? acc.Email : CursorAuth.LooksLikeEmail(acc.Label) ? acc.Label : null,
            membershipType: acc.MembershipType,
            displayName: acc.DisplayLabel);
        if (values.Values is null)
            return new CursorAuthApplyResult(false, values.Error);
        var target = CursorAuth.ResolveTarget();
        var running = target is not null && CursorAuth.IsRunning(target);
        if (running)
        {
            var answer = owner is null
                ? MessageBox.Show(CursorAuth.ConfirmCloseMessage, "登录到 Cursor", MessageBoxButtons.OKCancel, MessageBoxIcon.Question)
                : MessageBox.Show(owner, CursorAuth.ConfirmCloseMessage, "登录到 Cursor", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
            if (answer != DialogResult.OK)
                return new CursorAuthApplyResult(false, "已取消");
        }
        var token = acc.Token;
        var email = !string.IsNullOrWhiteSpace(acc.Email) ? acc.Email : CursorAuth.LooksLikeEmail(acc.Label) ? acc.Label : null;
        var membership = acc.MembershipType;
        var display = acc.DisplayLabel;
        return await Task.Run(() => CursorAuth.Apply(
            token,
            email: email,
            membershipType: membership,
            displayName: display,
            closeIfRunning: true,
            relaunch: true));
    }
}

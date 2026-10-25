using System.Net;
using System.Text;
using BSL.v41.General.Cloud;
using BSL.v41.Logic.Database;
using BSL.v41.Logic.Database.Account;
using BSL.v41.Logic.Environment.LaserListener;
using BSL.v41.Logic.Environment.LaserMessage.Sepo.Home.Laser.Laser;
using BSL.v41.Tools.LaserData;

namespace BSL.v41.General.Network;

/// <summary>
/// Local-only administrator panel. It is intentionally bound to localhost;
/// use an SSH tunnel when opening it remotely.
/// </summary>
public class HttpLaserSocketListener(string host, int port)
{
    public void StartSocket()
    {
        Console.WriteLine($"[ADMIN] Panel listening on http://{host}:{port}/");

        new Thread(() =>
        {
            var listener = new HttpListener();
            listener.Prefixes.Add($"http://{host}:{port}/");
            listener.Start();

            while (true)
            {
                try
                {
                    var context = listener.GetContext();
                    ThreadPool.QueueUserWorkItem(_ => Handle(context));
                }
                catch (HttpListenerException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
            }
        })
        { IsBackground = true }.Start();
    }

    private static void Handle(HttpListenerContext context)
    {
        try
        {
            if (context.Request.Url?.AbsolutePath.Equals("/account/login", StringComparison.OrdinalIgnoreCase) == true)
            {
                HandlePlayerLogin(context);
                return;
            }

            if (context.Request.Url?.AbsolutePath.Equals("/account/logout", StringComparison.OrdinalIgnoreCase) == true)
            {
                var cookie = context.Request.Cookies["bsl_player_session"]?.Value;
                if (!string.IsNullOrWhiteSpace(cookie)) PlayerCredentialStore.RevokeSession(cookie);
                context.Response.StatusCode = (int)HttpStatusCode.NoContent;
                context.Response.Close();
                return;
            }

            if (context.Request.Url?.AbsolutePath.Equals("/account/change-password", StringComparison.OrdinalIgnoreCase) == true)
            {
                HandlePlayerPasswordChange(context);
                return;
            }

            if (!IsAuthorized(context.Request))
            {
                context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
                context.Response.AddHeader("WWW-Authenticate", "Basic realm=\"BSL.v41 Admin\"");
                WriteResponse(context, "Yönetici paneli için yetkilendirme gerekli.", "text/plain; charset=utf-8");
                return;
            }

            var message = string.Empty;
            var lookup = string.Empty;
            if (context.Request.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase))
            {
                var form = ParseForm(context.Request);
                var action = form.GetValueOrDefault("action", string.Empty);
                var accountIdText = form.GetValueOrDefault("accountId", string.Empty);

                if (!long.TryParse(accountIdText, out var accountId) || accountId <= 0)
                {
                    message = "Geçerli bir hesap ID girin.";
                }
                else
                {
                    switch (action)
                    {
                        case "ban":
                            BannedAccountStore.Ban(accountId, form.GetValueOrDefault("reason", string.Empty));
                            var disconnected = InteractiveModule.DisconnectPlayer(accountId);
                            message = disconnected
                                ? $"#{accountId} banlandı ve aktif bağlantısı hemen kesildi."
                                : $"#{accountId} banlandı. Aktif bağlantı bulunamadı.";
                            break;
                        case "unban":
                            message = BannedAccountStore.Unban(accountId)
                                ? $"#{accountId} banı kaldırıldı."
                                : $"#{accountId} ban listesinde bulunamadı.";
                            break;
                        case "set_resource":
                            AdminAccountService.Change(accountId,
                                form.GetValueOrDefault("resource", string.Empty),
                                ParseAmount(form), true, out message);
                            break;
                        case "add_resource":
                            AdminAccountService.Change(accountId,
                                form.GetValueOrDefault("resource", string.Empty),
                                ParseAmount(form), false, out message);
                            break;
                        case "remove_resource":
                            AdminAccountService.Remove(accountId,
                                form.GetValueOrDefault("resource", string.Empty),
                                ParseAmount(form), out message);
                            break;
                        case "inbox_reward":
                            AdminAccountService.AddInboxReward(accountId,
                                form.GetValueOrDefault("reward", "Mega Kutu"), out message);
                            break;
                        case "generate_test_code":
                            if (accountId != 3)
                                message = "Test kodu yalnızca #3 hesabı için üretilebilir.";
                            else
                                TestCodeStore.TryGenerate(out _, out message);
                            break;
                        case "arm_account_switch":
                            AccountSwitchStore.TryArm(accountId,
                                form.GetValueOrDefault("testCode", string.Empty), out message);
                            break;
                        case "bind_local_identity":
                            LocalIdentityStore.TryBind(accountId,
                                form.GetValueOrDefault("email", string.Empty), out message);
                            break;
                        case "bind_credential":
                            PlayerCredentialStore.Bind(accountId,
                                form.GetValueOrDefault("username", string.Empty),
                                form.GetValueOrDefault("password", string.Empty), out message);
                            break;
                        case "change_credential_password":
                            PlayerCredentialStore.ChangePassword(
                                form.GetValueOrDefault("username", string.Empty),
                                form.GetValueOrDefault("currentPassword", string.Empty),
                                form.GetValueOrDefault("newPassword", string.Empty), out message);
                            break;
                        case "revoke_credential_sessions":
                            message = $"#{accountId} için {PlayerCredentialStore.RevokeSessions(accountId)} web oturumu kapatıldı.";
                            break;
                        case "lookup":
                            lookup = RenderAccount(accountId);
                            break;
                        default:
                            message = "Bilinmeyen yönetici işlemi.";
                            break;
                    }
                }
            }

            WriteResponse(context, RenderDashboard(message, lookup));
        }
        catch (Exception exception)
        {
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            WriteResponse(context, WebUtility.HtmlEncode(exception.Message), "text/plain; charset=utf-8");
        }
    }

    private static void HandlePlayerLogin(HttpListenerContext context)
    {
        if (!context.Request.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase))
        { context.Response.StatusCode = (int)HttpStatusCode.MethodNotAllowed; context.Response.Close(); return; }
        var form = ParseForm(context.Request);
        if (!PlayerCredentialStore.Authenticate(form.GetValueOrDefault("username", string.Empty),
                form.GetValueOrDefault("password", string.Empty), out var token, out var accountId, out var message))
        {
            context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
            WriteResponse(context, message, "text/plain; charset=utf-8");
            return;
        }
        context.Response.Cookies.Add(new Cookie("bsl_player_session", token) { HttpOnly = true, Path = "/", Secure = false });
        WriteResponse(context, $"Giriş başarılı. Hesap: #{accountId}", "text/plain; charset=utf-8");
    }

    private static void HandlePlayerPasswordChange(HttpListenerContext context)
    {
        if (!context.Request.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase))
        { context.Response.StatusCode = (int)HttpStatusCode.MethodNotAllowed; context.Response.Close(); return; }
        var cookie = context.Request.Cookies["bsl_player_session"]?.Value;
        if (string.IsNullOrWhiteSpace(cookie) || !PlayerCredentialStore.ValidateSession(cookie, out var accountId))
        { context.Response.StatusCode = (int)HttpStatusCode.Unauthorized; WriteResponse(context, "Oturum gerekli.", "text/plain; charset=utf-8"); return; }
        var form = ParseForm(context.Request);
        PlayerCredentialStore.ChangePasswordForAccount(accountId,
            form.GetValueOrDefault("currentPassword", string.Empty),
            form.GetValueOrDefault("newPassword", string.Empty), out var message);
        WriteResponse(context, message, "text/plain; charset=utf-8");
    }

    private static bool IsAuthorized(HttpListenerRequest request)
    {
        var expectedToken = Environment.GetEnvironmentVariable("BSL_ADMIN_TOKEN") ?? "local-only";
        var authorization = request.Headers["Authorization"];
        if (string.IsNullOrWhiteSpace(authorization) || !authorization.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
            return false;

        try
        {
            var credentials = Encoding.UTF8.GetString(Convert.FromBase64String(authorization[6..]));
            return credentials == $"admin:{expectedToken}";
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static Dictionary<string, string> ParseForm(HttpListenerRequest request)
    {
        using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
        var body = reader.ReadToEnd();
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var pair in body.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            var key = separator < 0 ? pair : pair[..separator];
            var value = separator < 0 ? string.Empty : pair[(separator + 1)..];
            values[Decode(key)] = Decode(value);
        }

        return values;
    }

    private static string Decode(string value)
    {
        return Uri.UnescapeDataString(value.Replace('+', ' '));
    }

    private static int ParseAmount(Dictionary<string, string> form)
    {
        return int.TryParse(form.GetValueOrDefault("amount", string.Empty), out var amount) ? amount : -1;
    }

    private static string RenderDashboard(string message, string lookup)
    {
        var banned = BannedAccountStore.Snapshot();
        var rows = banned.Count == 0
            ? "<tr><td colspan='3'>Ban listesi boş.</td></tr>"
            : string.Join(string.Empty, banned.Select(pair =>
                $"<tr><td>{pair.Key}</td><td>{WebUtility.HtmlEncode(pair.Value)}</td>" +
                $"<td><form method='post'><input type='hidden' name='action' value='unban'>" +
                $"<input type='hidden' name='accountId' value='{pair.Key}'><button>Banı kaldır</button></form></td></tr>"));

        var banner = string.IsNullOrWhiteSpace(message)
            ? string.Empty
            : $"<p class='message'>{WebUtility.HtmlEncode(message)}</p>";

        return $@"
                <!doctype html>
                <html lang='tr'><head><meta charset='utf-8'><title>BSL.v41 Yönetici Paneli</title>
                <style>body{{font-family:system-ui;background:#10131a;color:#eee;max-width:1000px;margin:30px auto;padding:0 16px}}
                section{{background:#1b2130;border-radius:12px;padding:18px;margin:14px 0}}input,button{{padding:9px;margin:4px;border-radius:6px;border:0}}
                button{{cursor:pointer;background:#4e8cff;color:#fff}}table{{width:100%;border-collapse:collapse}}td,th{{padding:8px;border-bottom:1px solid #394156;text-align:left}}
                .message{{background:#214d32;padding:12px;border-radius:8px}}.danger{{background:#8b3030}}</style></head>
                <body><h1>BSL.v41 Yönetici Paneli</h1>{banner}
                <section><h2>Durum</h2><p>Parmak izi: <b>{Fingerprint.GetMajorVersion()}.{Fingerprint.GetMinorVersion()}.{Fingerprint.GetBuildVersion()}</b>
                · Çevrimiçi: <b>{IdentifierListener.GetLogicGameListeners().Count}</b>
                · Hesap: <b>{Databases.AccountDatabase.GetMaxAccountId()}</b>
                · Banlı: <b>{banned.Count}</b> · Yönetici: <b>#1, #2, #3</b></p></section>
                <section><h2>Ban / ban kaldır</h2><form method='post'><input type='hidden' name='action' value='ban'>
                <input name='accountId' type='number' min='1' placeholder='Hesap ID' required>
                <input name='reason' placeholder='Sebep'><button class='danger'>Banla</button></form>
                <form method='post'><input type='hidden' name='action' value='unban'>
                <input name='accountId' type='number' min='1' placeholder='Hesap ID' required><button>Banı kaldır</button></form></section>
                <section><h2>Hesap kaynakları</h2>
                <p>Kupa, Elmas, Altın, Güç puanı ve Brawl Pass jetonlarını ayarlayabilir, artırabilir veya azaltabilirsin.</p>
                <form method='post'><input type='hidden' name='action' value='set_resource'>
                <input name='accountId' type='number' min='1' placeholder='Hesap ID' required>
                <select name='resource'><option value='trophies'>Kupa</option><option value='gems'>Elmas</option>
                <option value='gold'>Altın</option><option value='powerpoints'>Güç puanı</option><option value='brawlpass'>Brawl Pass jetonu</option></select>
                <input name='amount' type='number' min='0' placeholder='Yeni değer' required><button>Değeri ayarla</button></form>
                <form method='post'><input type='hidden' name='action' value='add_resource'>
                <input name='accountId' type='number' min='1' placeholder='Hesap ID' required>
                <select name='resource'><option value='trophies'>Kupa</option><option value='gems'>Elmas</option>
                <option value='gold'>Altın</option><option value='powerpoints'>Güç puanı</option><option value='brawlpass'>Brawl Pass jetonu</option></select>
                <input name='amount' type='number' min='0' placeholder='Eklenecek miktar' required><button>Ekle</button></form>
                <form method='post'><input type='hidden' name='action' value='remove_resource'>
                <input name='accountId' type='number' min='1' placeholder='Hesap ID' required>
                <select name='resource'><option value='trophies'>Kupa</option><option value='gems'>Elmas</option>
                <option value='gold'>Altın</option><option value='powerpoints'>Güç puanı</option><option value='brawlpass'>Brawl Pass jetonu</option></select>
                <input name='amount' type='number' min='0' placeholder='Azaltılacak miktar' required><button>Azalt</button></form></section>
                <section><h2>Gelen kutusuna ödül</h2><form method='post'>
                <input type='hidden' name='action' value='inbox_reward'>
                <input name='accountId' type='number' min='1' placeholder='Hesap ID' required>
                <select name='reward'><option>Mega Kutu</option><option>Büyük Kutu</option><option>Savaş Kutusu</option><option>Elmas paketi</option><option>Özel ödül</option></select>
                <button>Gelen kutusuna gönder</button></form></section>
                <section><h2>#3 test girişi</h2><p>Kod bellekte tutulur, 60 saniye geçerlidir ve tek kez kullanılabilir. Gerçek Supercell bilgileri kabul edilmez.</p>
                <form method='post'><input type='hidden' name='action' value='generate_test_code'>
                <input type='hidden' name='accountId' value='3'><button>Tek kullanımlık #3 kodu üret</button></form></section>
                <section><h2>Doğrulanmış oturumu #3'e geçir</h2><p>Önce yukarıdan kod üretin. Sonra şu an bağlı olan kaynak hesabı yazıp aynı kodu girin. Geçiş yalnızca doğrulanmış oturum ve aynı cihazla bir sonraki login'de bir kez uygulanır; IP değişikliği engel değildir.</p>
                <form method='post'><input type='hidden' name='action' value='arm_account_switch'>
                <input name='accountId' type='number' min='1' placeholder='Mevcut hesap ID' required>
                <input name='testCode' autocomplete='off' placeholder='Tek kullanımlık kod' required>
                <button>#3 geçişini hazırla</button></form></section>
                <section><h2>Yerel kimlik (#3)</h2><p>Bu kayıt yalnızca özel sunucunun Redis verisidir; gerçek Supercell ID hesabı oluşturmaz veya Supercell'e istek göndermez. E-posta düz metin olarak saklanmaz.</p>
                <form method='post'><input type='hidden' name='action' value='bind_local_identity'>
                <input type='hidden' name='accountId' value='3'>
                <input name='email' type='email' autocomplete='off' placeholder='Yerel e-posta etiketi' required>
                <button>#3'e bağla</button></form></section>
                <section><h2>Kalıcı oyuncu hesabı</h2>
                <p>Kullanıcı adı ve parola ayrı dosyada Argon2id hash olarak tutulur; oyun hesabı JSON'u değiştirilmez. Bu panel işlemi yönetici Basic Auth ile korunur.</p>
                <form method='post'><input type='hidden' name='action' value='bind_credential'>
                <input name='accountId' type='number' min='1' placeholder='Mevcut hesap ID' required>
                <input name='username' autocomplete='username' placeholder='Kullanıcı adı' required>
                <input name='password' type='password' autocomplete='new-password' placeholder='En az 12 karakter' required>
                <button>Hesaba bağla</button></form>
                <form method='post'><input type='hidden' name='action' value='change_credential_password'>
                <input name='username' autocomplete='username' placeholder='Kullanıcı adı' required>
                <input name='currentPassword' type='password' autocomplete='current-password' placeholder='Mevcut parola' required>
                <input name='newPassword' type='password' autocomplete='new-password' placeholder='Yeni parola' required>
                <button>Parolayı değiştir</button></form>
                <form method='post'><input type='hidden' name='action' value='revoke_credential_sessions'>
                <input name='accountId' type='number' min='1' placeholder='Hesap ID' required><button>Web oturumlarını kapat</button></form>
                <p>Bağlı kullanıcı adları: {RenderCredentialRows()}</p></section>
                <section><h2>Hesap sorgula</h2><form method='post'><input type='hidden' name='action' value='lookup'>
                <input name='accountId' type='number' min='1' placeholder='Hesap ID' required><button>Sorgula</button></form>{lookup}</section>
                <section><h2>Ban listesi</h2><table><tr><th>ID</th><th>Sebep</th><th>İşlem</th></tr>{rows}</table></section>
                </body></html>
                ";
    }

    private static string RenderAccount(long accountId)
    {
        var account = Databases.AccountDatabase.LoadAccount(accountId);
        if (account == null!) return $"<p>#{accountId} bulunamadı.</p>";

        var name = account.GetFieldValueByAccountStructureParameterFromAccountModel(AccountStructure.AvatarName);
        var trophies = account.GetFieldValueByAccountStructureParameterFromAccountModel(AccountStructure.Trophies);
        var status = account.GetFieldValueByAccountStructureParameterFromAccountModel(AccountStructure.PlayerStatus);
        var entries = account.GetFieldValueByAccountStructureParameterFromAccountModel(AccountStructure.IntValueEntryX)
            as Dictionary<int, IntValueEntry> ?? new();
        var gems = entries.GetValueOrDefault(16)?.Value ?? 0;
        var gold = entries.GetValueOrDefault(1)?.Value ?? 0;
        var brawlPassTokens = entries.GetValueOrDefault(28)?.Value ?? 0;
        var powerPoints = Math.Max(entries.GetValueOrDefault(12)?.Value ?? 0,
            entries.GetValueOrDefault(29)?.Value ?? 0);
        var online = IdentifierListener.GetV2LogicGameListenerByAccountId(accountId);
        return $"<p><b>#{accountId}</b> · İsim: {WebUtility.HtmlEncode(name?.ToString() ?? "-")} · " +
               $"Kupa: {trophies} · Elmas: {gems} · Altın: {gold} · Güç puanı: {powerPoints} · Brawl Pass jetonu: {brawlPassTokens} · " +
               $"PlayerStatus: {status} · Online: {online} · " +
               $"Yönetici: {AdminAccess.IsAdmin(accountId)} · Banlı: {BannedAccountStore.IsBanned(accountId)}</p>";
    }

    private static string RenderCredentialRows()
    {
        var rows = PlayerCredentialStore.Snapshot();
        return rows.Count == 0
            ? "yok"
            : string.Join(" · ", rows.Select(x => $"#{x.AccountId} {WebUtility.HtmlEncode(x.Username)}"));
    }

    private static void WriteResponse(HttpListenerContext context, string content, string contentType = "text/html; charset=utf-8")
    {
        var buffer = Encoding.UTF8.GetBytes(content);
        context.Response.ContentType = contentType;
        context.Response.ContentLength64 = buffer.Length;
        using var output = context.Response.OutputStream;
        output.Write(buffer, 0, buffer.Length);
    }
}

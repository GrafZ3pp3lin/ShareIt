using System.Text.Encodings.Web;
using System.Text.Unicode;
using kroet.ShareIt.AuthTwin.Realm;
using kroet.ShareIt.AuthTwin.Store;

namespace kroet.ShareIt.AuthTwin.Oidc;

/// <summary>
/// Keeps the ids that scripts and tests use on Keycloak's pages (<c>kc-form-login</c>, <c>username</c>,
/// <c>kc-login</c>, <c>instruction</c>). No password field. On top, a list of the users that signs one
/// in with a click.
/// </summary>
internal static class LoginPages
{
    public const string InvalidCredentials = "Invalid username or password.";

    public static IResult Form(RealmModel realm, string action, IEnumerable<TwinUser> users, string? error = null) =>
        Page(realm, StatusCodes.Status200OK, $"""
        <h1>Sign in to your account</h1>
        {(error is null ? "" : $"<p id=\"input-error\" class=\"error\">{Encode(error)}</p>")}
        <form class="users" method="post" action="{Encode(action)}">
        {string.Concat(users.Select(UserButton))}
        </form>
        <form id="kc-form-login" method="post" action="{Encode(action)}">
          <label for="username">Username or email</label>
          <input id="username" name="username" autofocus autocomplete="username">
          <button id="kc-login" type="submit">Sign in</button>
        </form>
        """);

    public static IResult Error(RealmModel realm, string message, int status = StatusCodes.Status400BadRequest) =>
        Page(realm, status, $"""<p class="instruction">{Encode(message)}</p>""");

    /// <param name="title">Keycloak titles its logout confirmation "Sign out" instead of the login title.</param>
    public static IResult Message(RealmModel realm, string message, string? title = null) =>
        Page(realm, StatusCodes.Status200OK, $"""<p class="instruction">{Encode(message)}</p>""", title);

    // Posts the same field the form does, so signing in by click takes the form's path, errors included.
    private static string UserButton(TwinUser user)
    {
        var name = $"{user.FirstName} {user.LastName}".Trim();
        var blocker = Blocker(user);
        return $"""
            <button name="username" value="{Encode(user.Username)}"{(blocker is null ? "" : " class=\"blocked\"")}>
              <strong>{Encode(name.Length > 0 ? name : user.Username)}</strong><span>{Encode(user.Email ?? "")}</span>
              {(blocker is null ? "" : $"<em>{Encode(blocker)}</em>")}
            </button>
            """;
    }

    /// <summary>Why Keycloak would issue this user no code, or null if it would.</summary>
    public static string? Blocker(TwinUser user) =>
        !user.Enabled ? "disabled"
        : !user.HasPassword ? "invite pending"
        : null;

    private static IResult Page(RealmModel realm, int status, string body, string? title = null) => Results.Content($"""
        <!DOCTYPE html>
        <html lang="{realm.DefaultLocale}">
        <head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
        <title>{Encode(title ?? $"Sign in to {realm.Name}")}</title>
        <style>{Style}</style></head>
        <body>
        <header>shareit <small>Auth-Twin</small></header>
        <main>
        {body}
        </main>
        </body>
        </html>
        """, "text/html; charset=utf-8", statusCode: status);

    // The frontend's palette, without its assets.
    private const string Style = """
        * { box-sizing: border-box; }
        body { margin: 0; min-height: 100vh; padding: 24px 16px; display: flex; flex-direction: column; justify-content: center;
          font: 15px/1.5 Inter, Helvetica, Arial, sans-serif; color: #121212;
          background: linear-gradient(180deg, #fcfcfc 0%, #f6f6f6 100%); }
        header { width: 100%; max-width: 520px; margin: 0 auto 24px; text-align: center; font-size: 22px; font-weight: 700; }
        header small { margin-left: 6px; padding: 2px 8px; border-radius: 999px; font-size: 12px; vertical-align: middle; color: #fff; background: #0e7490; }
        main { width: 100%; max-width: 520px; margin: 0 auto; padding: 28px; border: 1px solid #dcdcdc; border-radius: 16px; background: #fff;
          box-shadow: 0 8px 24px rgba(0, 0, 0, .05); }
        h1 { margin: 0 0 20px; font-size: 22px; }
        .users { display: grid; grid-template-columns: minmax(0, 1fr); gap: 8px; margin-bottom: 24px; }
        .users button { display: flex; align-items: baseline; gap: 10px; white-space: nowrap; padding: 12px 14px; text-align: left; font: inherit;
          color: inherit; border: 1px solid #dcdcdc; border-radius: 10px; background: #fafafa; cursor: pointer; }
        .users button:hover { border-color: #0e7490; }
        .users strong, .users span { min-width: 0; overflow: hidden; text-overflow: ellipsis; }
        .users span { flex-shrink: 100; color: #5f5f5f; font-size: 13px; }
        .users em { flex-shrink: 0; margin-left: auto; font-style: normal; font-size: 12px; padding: 1px 8px; border-radius: 999px;
          border: 1px solid #bae6fd; color: #0e7490; }
        .users .blocked { opacity: .55; }
        label { display: block; margin-bottom: 8px; font-size: 13px; font-weight: 700; }
        input { width: 100%; height: 48px; padding: 0 16px; font: inherit; color: #121212; border: 1px solid #dcdcdc; border-radius: 12px; background: #fafafa; }
        input:focus { outline: none; border-color: #0e7490; background: #fff; }
        #kc-login { width: 100%; height: 50px; margin-top: 16px; font: 700 16px Inter, Helvetica, Arial, sans-serif; color: #fff; border: 0;
          border-radius: 999px; background: #0e7490; cursor: pointer; }
        #kc-login:hover { background: #0891b2; }
        .error { padding: 10px 12px; border-radius: 10px; color: #9a3412; background: #ffedd5; }
        .instruction { margin: 0; }
        """;

    // Keeps umlauts readable; the default encoder would write them as character references.
    private static readonly HtmlEncoder Encoder = HtmlEncoder.Create(UnicodeRanges.All);

    private static string Encode(string value) => Encoder.Encode(value);
}

using System.Net;
using System.Security.Cryptography;

namespace FitLog.Infra;

public record UsuarioLogado(int Id, string Nome, string Perfil);

public static class Autenticacao
{
    private const string NomeCookie = "fitlog_token";

    private static readonly Dictionary<string, UsuarioLogado> Sessoes = new();

    public static void Entrar(HttpListenerContext ctx, UsuarioLogado usuario)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        Sessoes[token] = usuario;

        ctx.Response.SetCookie(new Cookie(NomeCookie, token) { HttpOnly = true, Path = "/" });
    }

    public static UsuarioLogado? Atual(HttpListenerContext ctx)
    {
        var token = ctx.Request.Cookies[NomeCookie]?.Value;
        if (token == null) return null;

        return Sessoes.TryGetValue(token, out var usuario) ? usuario : null;
    }

    public static void Sair(HttpListenerContext ctx)
    {
        var token = ctx.Request.Cookies[NomeCookie]?.Value;
        if (token != null) Sessoes.Remove(token);

        ctx.Response.SetCookie(new Cookie(NomeCookie, "")
        {
            HttpOnly = true,
            Path = "/",
            Expires = DateTime.UtcNow.AddDays(-1)
        });
    }

    public static UsuarioLogado? ExigirLogin(HttpListenerContext ctx)
    {
        var usuario = Atual(ctx);
        if (usuario == null)
            Http.Redirect(ctx, "/login");
        return usuario;
    }

    public static UsuarioLogado? ExigirAdmin(HttpListenerContext ctx)
    {
        var usuario = ExigirLogin(ctx);
        if (usuario != null && usuario.Perfil != "ADMIN")
        {
            Http.Html(ctx, Template.Pagina(ctx, "acesso_negado.html", new(), "Acesso negado"), 403);
            return null;
        }
        return usuario;
    }
}

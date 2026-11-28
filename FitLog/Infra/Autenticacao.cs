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

    // Quando um admin muda o perfil de alguém, as sessões abertas dessa pessoa passam a valer com o perfil novo
    public static void AtualizarPerfil(int idUsuario, string perfil)
    {
        foreach (var token in Sessoes.Keys.ToList())
            if (Sessoes[token].Id == idUsuario)
                Sessoes[token] = Sessoes[token] with { Perfil = perfil };
    }

    // Quando a pessoa muda o próprio nome, o menu passa a mostrar o nome novo
    public static void AtualizarNome(int idUsuario, string nome)
    {
        foreach (var token in Sessoes.Keys.ToList())
            if (Sessoes[token].Id == idUsuario)
                Sessoes[token] = Sessoes[token] with { Nome = nome };
    }

    // Usado ao excluir uma conta: quem estava logado com ela é deslogado
    public static void EncerrarSessoes(int idUsuario)
    {
        foreach (var token in Sessoes.Keys.ToList())
            if (Sessoes[token].Id == idUsuario)
                Sessoes.Remove(token);
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
            NegarAcesso(ctx);
            return null;
        }
        return usuario;
    }

    public static void NegarAcesso(HttpListenerContext ctx)
    {
        Http.Html(ctx, Template.Pagina(ctx, "acesso_negado.html", new(), "Acesso negado"), 403);
    }

    // Plano predefinido: só o admin altera. Plano personalizado: só quem está associado a ele.
    public static bool PodeAlterarPlano(UsuarioLogado usuario, int idPlano)
    {
        var resultado = Database.Consultar("""
            SELECT COUNT(*) AS qtd
            FROM plano p
            WHERE p.id_plano = :id_plano
              AND (   (p.tipo = 'PREDEFINIDO' AND :admin = 1)
                   OR (p.tipo = 'PERSONALIZADO' AND EXISTS (
                          SELECT 1 FROM usuario_plano up
                          WHERE up.id_plano = p.id_plano AND up.id_usuario = :id_usuario)))
            """,
            ("id_plano", idPlano),
            ("admin", usuario.Perfil == "ADMIN" ? 1 : 0),
            ("id_usuario", usuario.Id));

        return Convert.ToInt32(resultado[0]["QTD"]) > 0;
    }

    // Ficha avulsa: só o dono. Ficha de plano: segue a regra do plano.
    public static bool PodeAlterarFicha(UsuarioLogado usuario, int idTreino)
    {
        var resultado = Database.Consultar("""
            SELECT COUNT(*) AS qtd
            FROM treino t
            LEFT JOIN plano p ON p.id_plano = t.id_plano
            WHERE t.id_treino = :id_treino
              AND (   (t.tipo_treino = 'AVULSO' AND t.id_usuario = :id_usuario)
                   OR (p.tipo = 'PREDEFINIDO' AND :admin = 1)
                   OR (p.tipo = 'PERSONALIZADO' AND EXISTS (
                          SELECT 1 FROM usuario_plano up
                          WHERE up.id_plano = p.id_plano AND up.id_usuario = :id_usuario)))
            """,
            ("id_treino", idTreino),
            ("admin", usuario.Perfil == "ADMIN" ? 1 : 0),
            ("id_usuario", usuario.Id));

        return Convert.ToInt32(resultado[0]["QTD"]) > 0;
    }
}

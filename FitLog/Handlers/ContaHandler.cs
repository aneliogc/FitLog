using FitLog.Infra;
using System.Globalization;
using System.Net;

namespace FitLog.Handlers;

public static class ContaHandler
{
    // GET /login
    public static void FormularioLogin(HttpListenerContext ctx)
    {
        if (Autenticacao.Atual(ctx) != null)
        {
            Http.Redirect(ctx, "/");
            return;
        }

        Http.Html(ctx, PaginaLogin(ctx, "", ""));
    }

    // POST /login
    public static void Entrar(HttpListenerContext ctx)
    {
        var form = Http.Form(ctx);
        var login = form["login"].Trim();
        var senha = form["senha"];

        var usuarios = Database.Consultar(
            "SELECT id_usuario, nome, senha, perfil FROM usuario WHERE login = :login",
            ("login", login));

        if (usuarios.Count == 0 || !Senha.Verificar(senha, usuarios[0]["SENHA"]!.ToString()!))
        {
            Http.Html(ctx, PaginaLogin(ctx, login, "Login ou senha incorretos."));
            return;
        }

        var u = usuarios[0];
        Autenticacao.Entrar(ctx, new UsuarioLogado(
            Convert.ToInt32(u["ID_USUARIO"]),
            u["NOME"]!.ToString()!,
            u["PERFIL"]!.ToString()!));

        Http.Redirect(ctx, "/");
    }

    // POST /logout
    public static void Sair(HttpListenerContext ctx)
    {
        Autenticacao.Sair(ctx);
        Http.Redirect(ctx, "/login");
    }

    // GET /cadastro
    public static void FormularioCadastro(HttpListenerContext ctx)
    {
        if (Autenticacao.Atual(ctx) != null)
        {
            Http.Redirect(ctx, "/");
            return;
        }

        Http.Html(ctx, PaginaCadastro(ctx, "", "", "", "", ""));
    }

    // POST /cadastro
    public static void Cadastrar(HttpListenerContext ctx)
    {
        var form = Http.Form(ctx);
        var nome = form["nome"].Trim();
        var email = form["email"].Trim();
        var login = form["login"].Trim();
        var senha = form["senha"];
        var dataNasc = form["data_nasc"]; 

        try
        {
            Database.Executar("""
                INSERT INTO usuario (nome, email, login, senha, data_nasc)
                VALUES (:nome, :email, :login, :senha, :data_nasc)
                """,
                ("nome", nome),
                ("email", email),
                ("login", login),
                ("senha", Senha.GerarHash(senha)),
                ("data_nasc", dataNasc == "" ? null : DateTime.ParseExact(dataNasc, "yyyy-MM-dd", CultureInfo.InvariantCulture)));

            Http.Redirect(ctx, "/login");
        }
        catch (Exception ex)
        {
            Http.Html(ctx, PaginaCadastro(ctx, nome, email, login, dataNasc, Erros.Traduzir(ex)));
        }
    }

    private static string PaginaLogin(HttpListenerContext ctx, string login, string erro)
    {
        return Template.Pagina(ctx, "login.html", new()
        {
            ["ALERTA"] = Template.Alerta(erro),
            ["LOGIN"] = WebUtility.HtmlEncode(login)
        }, "Entrar");
    }

    private static string PaginaCadastro(HttpListenerContext ctx, string nome, string email, string login, string dataNasc, string erro)
    {
        return Template.Pagina(ctx, "cadastro.html", new()
        {
            ["ALERTA"] = Template.Alerta(erro),
            ["NOME"] = WebUtility.HtmlEncode(nome),
            ["EMAIL"] = WebUtility.HtmlEncode(email),
            ["LOGIN"] = WebUtility.HtmlEncode(login),
            ["DATA_NASC"] = WebUtility.HtmlEncode(dataNasc)
        }, "Criar conta");
    }
}

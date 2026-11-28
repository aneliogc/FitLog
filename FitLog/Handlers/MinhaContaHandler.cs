using FitLog.Infra;
using System.Globalization;
using System.Net;

namespace FitLog.Handlers;

// Minha conta: o usuário logado altera os próprios dados, a senha ou exclui a conta
public static class MinhaContaHandler
{
    // GET /minha-conta
    public static void Exibir(HttpListenerContext ctx)
    {
        var usuario = Autenticacao.ExigirLogin(ctx);
        if (usuario == null) return;

        // Depois de salvar, o redirecionamento volta com ?ok= para mostrar a confirmação
        var ok = Http.Query(ctx, "ok");
        var sucesso = ok == "dados" ? "Dados atualizados."
                    : ok == "senha" ? "Senha alterada."
                    : "";

        var u = Database.Consultar(
            "SELECT nome, email, login, data_nasc FROM usuario WHERE id_usuario = :id",
            ("id", usuario.Id))[0];

        Http.Html(ctx, Pagina(ctx,
            u["NOME"]!.ToString()!,
            u["EMAIL"]!.ToString()!,
            u["LOGIN"]!.ToString()!,
            u["DATA_NASC"] is DateTime d ? d.ToString("yyyy-MM-dd") : "",
            sucesso, ""));
    }

    // POST /minha-conta/salvar
    public static void SalvarDados(HttpListenerContext ctx)
    {
        var usuario = Autenticacao.ExigirLogin(ctx);
        if (usuario == null) return;

        var form = Http.Form(ctx);
        var nome = form["nome"].Trim();
        var email = form["email"].Trim();
        var dataNasc = form["data_nasc"];

        try
        {
            // O WHERE pelo usuário logado garante que cada um só altera a própria conta
            Database.Executar("""
                UPDATE usuario
                SET nome = :nome, email = :email, data_nasc = :data_nasc
                WHERE id_usuario = :id
                """,
                ("nome", nome),
                ("email", email),
                ("data_nasc", DateTime.ParseExact(dataNasc, "yyyy-MM-dd", CultureInfo.InvariantCulture)),
                ("id", usuario.Id));

            Autenticacao.AtualizarNome(usuario.Id, nome);
            Http.Redirect(ctx, "/minha-conta?ok=dados");
        }
        catch (Exception ex)
        {
            Http.Html(ctx, Pagina(ctx, nome, email, Login(usuario.Id), dataNasc, "", Erros.Traduzir(ex)));
        }
    }

    // GET /minha-conta/senha
    public static void FormularioSenha(HttpListenerContext ctx)
    {
        if (Autenticacao.ExigirLogin(ctx) == null) return;

        Http.Html(ctx, PaginaSenha(ctx, ""));
    }

    // POST /minha-conta/senha
    public static void AlterarSenha(HttpListenerContext ctx)
    {
        var usuario = Autenticacao.ExigirLogin(ctx);
        if (usuario == null) return;

        var form = Http.Form(ctx);
        var senhaAtual = form["senha_atual"];
        var novaSenha = form["nova_senha"];
        var confirmacao = form["confirmacao"];

        var guardada = Database.Consultar(
            "SELECT senha FROM usuario WHERE id_usuario = :id",
            ("id", usuario.Id))[0]["SENHA"]!.ToString()!;

        string erro = "";
        if (!Senha.Verificar(senhaAtual, guardada))
            erro = "A senha atual está incorreta.";
        else if (novaSenha != confirmacao)
            erro = "A nova senha e a confirmação não são iguais.";

        if (erro != "")
        {
            Http.Html(ctx, PaginaSenha(ctx, erro));
            return;
        }

        Database.Executar(
            "UPDATE usuario SET senha = :senha WHERE id_usuario = :id",
            ("senha", Senha.GerarHash(novaSenha)), ("id", usuario.Id));

        Http.Redirect(ctx, "/minha-conta?ok=senha");
    }

    // POST /minha-conta/excluir
    public static void Excluir(HttpListenerContext ctx)
    {
        var usuario = Autenticacao.ExigirLogin(ctx);
        if (usuario == null) return;

        UsuarioHandler.ExcluirConta(usuario.Id);
        Autenticacao.Sair(ctx);
        Http.Redirect(ctx, "/login");
    }

    private static string Login(int idUsuario)
    {
        return Database.Consultar("SELECT login FROM usuario WHERE id_usuario = :id", ("id", idUsuario))[0]["LOGIN"]!.ToString()!;
    }

    private static string Pagina(HttpListenerContext ctx, string nome, string email, string login, string dataNasc,
                                 string sucesso, string erroDados)
    {
        return Template.Pagina(ctx, "minha_conta.html", new()
        {
            ["SUCESSO"] = Template.Sucesso(sucesso),
            ["ALERTA_DADOS"] = Template.Alerta(erroDados),
            ["NOME"] = WebUtility.HtmlEncode(nome),
            ["EMAIL"] = WebUtility.HtmlEncode(email),
            ["LOGIN"] = WebUtility.HtmlEncode(login),
            ["DATA_NASC"] = WebUtility.HtmlEncode(dataNasc)
        }, "Minha conta");
    }

    private static string PaginaSenha(HttpListenerContext ctx, string erro)
    {
        return Template.Pagina(ctx, "alterar_senha.html", new()
        {
            ["ALERTA"] = Template.Alerta(erro)
        }, "Alterar senha");
    }
}

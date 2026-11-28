using System.Net;

namespace FitLog.Infra;

public static class Template
{
    private static readonly string Pasta = Path.Combine(AppContext.BaseDirectory, "Templates");

    public static string Pagina(HttpListenerContext ctx, string arquivo, Dictionary<string, string> valores, string titulo)
    {
        return Preencher("layout.html", new()
        {
            ["TITULO"] = WebUtility.HtmlEncode(titulo),
            ["MENU"] = Menu(Autenticacao.Atual(ctx)),
            ["CONTEUDO"] = Preencher(arquivo, valores)
        });
    }

    public static string Alerta(string mensagem)
    {
        if (mensagem == "") return "";
        return $"<div class=\"alert alert-danger\">{WebUtility.HtmlEncode(mensagem)}</div>";
    }

    public static string Sucesso(string mensagem)
    {
        if (mensagem == "") return "";
        return $"<div class=\"alert alert-success\">{WebUtility.HtmlEncode(mensagem)}</div>";
    }

    private static string Menu(UsuarioLogado? usuario)
    {
        if (usuario == null) return "";

        return Preencher("menu.html", new()
        {
            ["NOME"] = WebUtility.HtmlEncode(usuario.Nome),
            ["ADMIN"] = usuario.Perfil == "ADMIN" ? Preencher("menu_admin.html", new()) : ""
        });
    }

    // Lê um arquivo da pasta Templates e troca cada {{CHAVE}} pelo valor
    private static string Preencher(string arquivo, Dictionary<string, string> valores)
    {
        var html = File.ReadAllText(Path.Combine(Pasta, arquivo));
        foreach (var (chave, valor) in valores)
            html = html.Replace("{{" + chave + "}}", valor);
        return html;
    }
}

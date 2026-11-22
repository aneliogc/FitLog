using FitLog.Infra;
using System.Net;
using System.Text;

namespace FitLog.Handlers;

public static class GrupoHandler
{
    // GET /grupos
    public static void Listar(HttpListenerContext ctx)
    {
        if (Autenticacao.ExigirAdmin(ctx) == null) return;

        var grupos = Database.Consultar("SELECT id_grupo, nome FROM grupo_muscular ORDER BY nome");

        var linhas = new StringBuilder();
        foreach (var g in grupos)
        {
            linhas.Append("<tr>");
            linhas.Append($"<td>{g["ID_GRUPO"]}</td>");
            linhas.Append($"<td>{WebUtility.HtmlEncode(g["NOME"]?.ToString())}</td>");
            linhas.Append("</tr>");
        }

        Http.Html(ctx, Template.Pagina(ctx, "grupos.html", new()
        {
            ["LINHAS"] = linhas.ToString()
        }, "Grupos musculares"));
    }

    // GET /grupos/form
    public static void Formulario(HttpListenerContext ctx)
    {
        if (Autenticacao.ExigirAdmin(ctx) == null) return;

        Http.Html(ctx, PaginaFormulario(ctx, "", ""));
    }

    // POST /grupos/salvar
    public static void Salvar(HttpListenerContext ctx)
    {
        if (Autenticacao.ExigirAdmin(ctx) == null) return;

        var form = Http.Form(ctx);
        var nome = form["nome"].Trim();

        try
        {
            Database.Executar("INSERT INTO grupo_muscular (nome) VALUES (:nome)", ("nome", nome));
            Http.Redirect(ctx, "/grupos");
        }
        catch (Exception ex)
        {
            Http.Html(ctx, PaginaFormulario(ctx, nome, Erros.Traduzir(ex)));
        }
    }

    private static string PaginaFormulario(HttpListenerContext ctx, string nome, string erro)
    {
        return Template.Pagina(ctx, "grupo_form.html", new()
        {
            ["ALERTA"] = Template.Alerta(erro),
            ["NOME"] = WebUtility.HtmlEncode(nome)
        }, "Novo grupo muscular");
    }
}

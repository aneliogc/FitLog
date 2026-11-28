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

        Http.Html(ctx, PaginaLista(ctx, ""));
    }

    // GET /grupos/form e /grupos/form?id=
    public static void Formulario(HttpListenerContext ctx)
    {
        if (Autenticacao.ExigirAdmin(ctx) == null) return;

        var id = Http.Query(ctx, "id");
        if (id == "")
        {
            Http.Html(ctx, PaginaFormulario(ctx, "", "", ""));
            return;
        }

        var grupos = Database.Consultar(
            "SELECT nome FROM grupo_muscular WHERE id_grupo = :id",
            ("id", int.Parse(id)));

        if (grupos.Count == 0)
        {
            Http.Redirect(ctx, "/grupos");
            return;
        }

        Http.Html(ctx, PaginaFormulario(ctx, id, grupos[0]["NOME"]!.ToString()!, ""));
    }

    // POST /grupos/salvar
    public static void Salvar(HttpListenerContext ctx)
    {
        if (Autenticacao.ExigirAdmin(ctx) == null) return;

        var form = Http.Form(ctx);
        var id = form["id"];
        var nome = form["nome"].Trim();

        try
        {
            if (id == "")
            {
                Database.Executar("INSERT INTO grupo_muscular (nome) VALUES (:nome)", ("nome", nome));
            }
            else
            {
                Database.Executar(
                    "UPDATE grupo_muscular SET nome = :nome WHERE id_grupo = :id",
                    ("nome", nome),
                    ("id", int.Parse(id)));
            }

            Http.Redirect(ctx, "/grupos");
        }
        catch (Exception ex)
        {
            Http.Html(ctx, PaginaFormulario(ctx, id, nome, Erros.Traduzir(ex)));
        }
    }

    // POST /grupos/excluir
    public static void Excluir(HttpListenerContext ctx)
    {
        if (Autenticacao.ExigirAdmin(ctx) == null) return;

        var form = Http.Form(ctx);

        try
        {
            Database.Executar("DELETE FROM grupo_muscular WHERE id_grupo = :id", ("id", int.Parse(form["id"])));
            Http.Redirect(ctx, "/grupos");
        }
        catch (Exception ex)
        {
            // Grupo usado por algum exercício: ORA-02292, mostrado em cima da lista
            Http.Html(ctx, PaginaLista(ctx, Erros.Traduzir(ex)));
        }
    }

    private static string PaginaLista(HttpListenerContext ctx, string erro)
    {
        var grupos = Database.Consultar("SELECT id_grupo, nome FROM grupo_muscular ORDER BY nome");

        var linhas = new StringBuilder();
        foreach (var g in grupos)
        {
            var id = g["ID_GRUPO"];
            linhas.Append("<tr>");
            linhas.Append($"<td>{WebUtility.HtmlEncode(g["NOME"]?.ToString())}</td>");
            linhas.Append($"""
                <td class="text-end">
                  <a class="btn btn-sm btn-outline-primary" href="/grupos/form?id={id}">Editar</a>
                  <form method="post" action="/grupos/excluir" class="d-inline" onsubmit="return confirm('Excluir este grupo muscular?')">
                    <input type="hidden" name="id" value="{id}">
                    <button class="btn btn-sm btn-outline-danger">Excluir</button>
                  </form>
                </td>
                """);
            linhas.Append("</tr>");
        }

        if (grupos.Count == 0)
            linhas.Append("<tr><td colspan=\"2\" class=\"text-body-secondary\">Nenhum grupo cadastrado.</td></tr>");

        return Template.Pagina(ctx, "grupos.html", new()
        {
            ["ALERTA"] = Template.Alerta(erro),
            ["LINHAS"] = linhas.ToString()
        }, "Grupos musculares");
    }

    private static string PaginaFormulario(HttpListenerContext ctx, string id, string nome, string erro)
    {
        var titulo = id == "" ? "Novo grupo muscular" : "Editar grupo muscular";

        return Template.Pagina(ctx, "grupo_form.html", new()
        {
            ["TITULO_FORM"] = titulo,
            ["ALERTA"] = Template.Alerta(erro),
            ["ID"] = WebUtility.HtmlEncode(id),
            ["NOME"] = WebUtility.HtmlEncode(nome)
        }, titulo);
    }
}

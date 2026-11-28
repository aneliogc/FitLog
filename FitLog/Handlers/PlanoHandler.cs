using FitLog.Infra;
using System.Net;
using System.Text;

namespace FitLog.Handlers;

// Planos predefinidos (área do administrador)
public static class PlanoHandler
{
    // GET /planos
    public static void Listar(HttpListenerContext ctx)
    {
        if (Autenticacao.ExigirAdmin(ctx) == null) return;

        Http.Html(ctx, PaginaLista(ctx, ""));
    }

    // GET /planos/form e /planos/form?id=
    public static void Formulario(HttpListenerContext ctx)
    {
        if (Autenticacao.ExigirAdmin(ctx) == null) return;

        var id = Http.Query(ctx, "id");
        if (id == "")
        {
            Http.Html(ctx, PaginaFormulario(ctx, "", "", ""));
            return;
        }

        var planos = Database.Consultar(
            "SELECT nome FROM plano WHERE id_plano = :id AND tipo = 'PREDEFINIDO'",
            ("id", int.Parse(id)));

        if (planos.Count == 0)
        {
            Http.Redirect(ctx, "/planos");
            return;
        }

        Http.Html(ctx, PaginaFormulario(ctx, id, planos[0]["NOME"]!.ToString()!, ""));
    }

    // POST /planos/salvar
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
                Database.Executar("INSERT INTO plano (nome, tipo) VALUES (:nome, 'PREDEFINIDO')", ("nome", nome));
            }
            else
            {
                // O filtro pelo tipo impede alterar um plano personalizado mudando o id na página
                Database.Executar(
                    "UPDATE plano SET nome = :nome WHERE id_plano = :id AND tipo = 'PREDEFINIDO'",
                    ("nome", nome), ("id", int.Parse(id)));
            }

            Http.Redirect(ctx, "/planos");
        }
        catch (Exception ex)
        {
            Http.Html(ctx, PaginaFormulario(ctx, id, nome, Erros.Traduzir(ex)));
        }
    }

    // POST /planos/excluir
    public static void Excluir(HttpListenerContext ctx)
    {
        if (Autenticacao.ExigirAdmin(ctx) == null) return;

        try
        {
            // As fichas do plano e as associações com usuários saem em cascata
            Database.Executar(
                "DELETE FROM plano WHERE id_plano = :id AND tipo = 'PREDEFINIDO'",
                ("id", int.Parse(Http.Form(ctx)["id"])));
            Http.Redirect(ctx, "/planos");
        }
        catch (Exception ex)
        {
            Http.Html(ctx, PaginaLista(ctx, Erros.Traduzir(ex)));
        }
    }

    private static string PaginaLista(HttpListenerContext ctx, string erro)
    {
        var planos = Database.Consultar("""
            SELECT p.id_plano, p.nome,
                   (SELECT COUNT(*) FROM treino t        WHERE t.id_plano  = p.id_plano) AS fichas,
                   (SELECT COUNT(*) FROM usuario_plano up WHERE up.id_plano = p.id_plano) AS seguidores
            FROM plano p
            WHERE p.tipo = 'PREDEFINIDO'
            ORDER BY p.nome
            """);

        var linhas = new StringBuilder();
        foreach (var p in planos)
        {
            var id = p["ID_PLANO"];
            linhas.Append("<tr>");
            linhas.Append($"<td>{WebUtility.HtmlEncode(p["NOME"]?.ToString())}</td>");
            linhas.Append($"<td>{p["FICHAS"]}</td>");
            linhas.Append($"<td>{p["SEGUIDORES"]}</td>");
            linhas.Append($"""
                <td class="text-end">
                  <a class="btn btn-sm btn-outline-secondary" href="/planos/fichas?id={id}">Fichas</a>
                  <a class="btn btn-sm btn-outline-primary" href="/planos/form?id={id}">Editar</a>
                  <form method="post" action="/planos/excluir" class="d-inline" onsubmit="return confirm('Excluir o plano e todas as fichas dele? Quem segue o plano deixará de segui-lo.')">
                    <input type="hidden" name="id" value="{id}">
                    <button class="btn btn-sm btn-outline-danger">Excluir</button>
                  </form>
                </td>
                """);
            linhas.Append("</tr>");
        }

        if (planos.Count == 0)
            linhas.Append("<tr><td colspan=\"4\" class=\"text-body-secondary\">Nenhum plano predefinido cadastrado.</td></tr>");

        return Template.Pagina(ctx, "planos.html", new()
        {
            ["ALERTA"] = Template.Alerta(erro),
            ["LINHAS"] = linhas.ToString()
        }, "Planos predefinidos");
    }

    private static string PaginaFormulario(HttpListenerContext ctx, string id, string nome, string erro)
    {
        var titulo = id == "" ? "Novo plano predefinido" : "Editar plano predefinido";

        return Template.Pagina(ctx, "plano_form.html", new()
        {
            ["TITULO_FORM"] = titulo,
            ["ALERTA"] = Template.Alerta(erro),
            ["ID"] = WebUtility.HtmlEncode(id),
            ["NOME"] = WebUtility.HtmlEncode(nome)
        }, titulo);
    }
}

using FitLog.Infra;
using System.Net;
using System.Text;

namespace FitLog.Handlers;

// Planos personalizados de um usuário, montados pelo administrador (como um personal montando o plano do aluno).
// O plano pertence ao usuário (usuario_plano) e aparece em "Meus planos personalizados" dele.
public static class UsuarioPlanoHandler
{
    // GET /usuarios/planos?id=
    public static void Listar(HttpListenerContext ctx)
    {
        if (Autenticacao.ExigirAdmin(ctx) == null) return;

        var idUsuario = int.Parse(Http.Query(ctx, "id"));
        var pagina = PaginaLista(ctx, idUsuario, "");
        if (pagina == null) Http.Redirect(ctx, "/usuarios");
        else Http.Html(ctx, pagina);
    }

    // GET /usuarios/planos/form?usuario=  e  /usuarios/planos/form?usuario=&id=
    public static void Formulario(HttpListenerContext ctx)
    {
        if (Autenticacao.ExigirAdmin(ctx) == null) return;

        var idUsuario = int.Parse(Http.Query(ctx, "usuario"));
        var id = Http.Query(ctx, "id");
        var nome = "";

        if (id != "")
        {
            var planos = Database.Consultar("""
                SELECT p.nome FROM plano p
                JOIN usuario_plano up ON up.id_plano = p.id_plano
                WHERE p.id_plano = :id AND p.tipo = 'PERSONALIZADO' AND up.id_usuario = :id_usuario
                """, ("id", int.Parse(id)), ("id_usuario", idUsuario));

            if (planos.Count == 0)
            {
                Http.Redirect(ctx, $"/usuarios/planos?id={idUsuario}");
                return;
            }
            nome = planos[0]["NOME"]!.ToString()!;
        }

        Http.Html(ctx, PaginaFormulario(ctx, idUsuario, id, nome, ""));
    }

    // POST /usuarios/planos/salvar
    public static void Salvar(HttpListenerContext ctx)
    {
        if (Autenticacao.ExigirAdmin(ctx) == null) return;

        var form = Http.Form(ctx);
        var idUsuario = int.Parse(form["id_usuario"]);
        var id = form["id_plano"];
        var nome = form["nome"].Trim();

        try
        {
            if (id != "")
            {
                Database.Executar("""
                    UPDATE plano SET nome = :nome
                    WHERE id_plano = :id AND tipo = 'PERSONALIZADO'
                      AND EXISTS (SELECT 1 FROM usuario_plano up WHERE up.id_plano = plano.id_plano AND up.id_usuario = :id_usuario)
                    """,
                    ("nome", nome), ("id", int.Parse(id)), ("id_usuario", idUsuario));
                Http.Redirect(ctx, $"/usuarios/planos?id={idUsuario}");
                return;
            }

            int novoId = 0;
            Database.Transacao(conn =>
            {
                novoId = Database.InserirComId(conn,
                    "INSERT INTO plano (nome, tipo) VALUES (:nome, 'PERSONALIZADO')", "id_plano",
                    ("nome", nome));

                Database.Executar(conn,
                    "INSERT INTO usuario_plano (id_usuario, id_plano) VALUES (:id_usuario, :id_plano)",
                    ("id_usuario", idUsuario), ("id_plano", novoId));
            });

            // Plano novo ainda não tem treinos: vai direto para a tela de treinos dele
            Http.Redirect(ctx, $"/planos/fichas?id={novoId}");
        }
        catch (Exception ex)
        {
            Http.Html(ctx, PaginaFormulario(ctx, idUsuario, id, nome, Erros.Traduzir(ex)));
        }
    }

    // POST /usuarios/planos/excluir
    public static void Excluir(HttpListenerContext ctx)
    {
        if (Autenticacao.ExigirAdmin(ctx) == null) return;

        var form = Http.Form(ctx);
        var idUsuario = int.Parse(form["id_usuario"]);

        try
        {
            // Os treinos do plano e a associação saem em cascata
            Database.Executar("""
                DELETE FROM plano
                WHERE id_plano = :id AND tipo = 'PERSONALIZADO'
                  AND EXISTS (SELECT 1 FROM usuario_plano up WHERE up.id_plano = plano.id_plano AND up.id_usuario = :id_usuario)
                """,
                ("id", int.Parse(form["id_plano"])), ("id_usuario", idUsuario));
            Http.Redirect(ctx, $"/usuarios/planos?id={idUsuario}");
        }
        catch (Exception ex)
        {
            Http.Html(ctx, PaginaLista(ctx, idUsuario, Erros.Traduzir(ex)) ?? "");
        }
    }

    // Devolve null se o usuário não existir
    private static string? PaginaLista(HttpListenerContext ctx, int idUsuario, string erro)
    {
        var usuarios = Database.Consultar("SELECT nome FROM usuario WHERE id_usuario = :id", ("id", idUsuario));
        if (usuarios.Count == 0) return null;

        var planos = Database.Consultar("""
            SELECT p.id_plano, p.nome, up.ativo,
                   (SELECT COUNT(*) FROM treino t WHERE t.id_plano = p.id_plano) AS treinos
            FROM plano p
            JOIN usuario_plano up ON up.id_plano = p.id_plano
            WHERE up.id_usuario = :id_usuario AND p.tipo = 'PERSONALIZADO'
            ORDER BY p.nome
            """, ("id_usuario", idUsuario));

        var linhas = new StringBuilder();
        foreach (var p in planos)
        {
            var id = p["ID_PLANO"];
            var ativo = Convert.ToInt32(p["ATIVO"]) == 1
                ? "<span class=\"badge text-bg-success ms-2\">Ativo</span>"
                : "";

            linhas.Append($"""
                <tr>
                  <td>{WebUtility.HtmlEncode(p["NOME"]?.ToString())}{ativo}</td>
                  <td>{p["TREINOS"]}</td>
                  <td class="text-end text-nowrap">
                    <a class="btn btn-sm btn-outline-secondary" href="/planos/fichas?id={id}">Treinos</a>
                    <a class="btn btn-sm btn-outline-primary" href="/usuarios/planos/form?usuario={idUsuario}&id={id}">Renomear</a>
                    <form method="post" action="/usuarios/planos/excluir" class="d-inline" onsubmit="return confirm('Excluir este plano e todos os treinos dele?')">
                      <input type="hidden" name="id_usuario" value="{idUsuario}">
                      <input type="hidden" name="id_plano" value="{id}">
                      <button class="btn btn-sm btn-outline-danger">Excluir</button>
                    </form>
                  </td>
                </tr>
                """);
        }

        if (planos.Count == 0)
            linhas.Append("<tr><td colspan=\"3\" class=\"text-body-secondary\">Este usuário não tem planos personalizados.</td></tr>");

        var nome = usuarios[0]["NOME"]!.ToString()!;
        return Template.Pagina(ctx, "usuario_planos.html", new()
        {
            ["USUARIO"] = WebUtility.HtmlEncode(nome),
            ["ID_USUARIO"] = idUsuario.ToString(),
            ["ALERTA"] = Template.Alerta(erro),
            ["LINHAS"] = linhas.ToString()
        }, "Planos de " + nome);
    }

    private static string PaginaFormulario(HttpListenerContext ctx, int idUsuario, string id, string nome, string erro)
    {
        var usuario = Database.Consultar("SELECT nome FROM usuario WHERE id_usuario = :id", ("id", idUsuario));
        var nomeUsuario = usuario.Count == 0 ? "" : usuario[0]["NOME"]!.ToString()!;
        var titulo = id == "" ? "Novo plano para " + nomeUsuario : "Renomear plano";

        return Template.Pagina(ctx, "usuario_plano_form.html", new()
        {
            ["TITULO_FORM"] = WebUtility.HtmlEncode(titulo),
            ["ALERTA"] = Template.Alerta(erro),
            ["ID_USUARIO"] = idUsuario.ToString(),
            ["ID"] = WebUtility.HtmlEncode(id),
            ["NOME"] = WebUtility.HtmlEncode(nome),
            ["AJUDA"] = id == "" ? "Depois de salvar, você vai para a tela de treinos do plano. O usuário verá o plano em Meus planos e poderá ativá-lo." : ""
        }, titulo);
    }
}

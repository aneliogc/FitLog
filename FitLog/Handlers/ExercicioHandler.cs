using FitLog.Infra;
using System.Net;
using System.Text;

namespace FitLog.Handlers;

public static class ExercicioHandler
{
    // GET /exercicios
    public static void Listar(HttpListenerContext ctx)
    {
        if (Autenticacao.ExigirAdmin(ctx) == null) return;

        Http.Html(ctx, PaginaLista(ctx, ""));
    }

    // GET /exercicios/form e /exercicios/form?id=
    public static void Formulario(HttpListenerContext ctx)
    {
        if (Autenticacao.ExigirAdmin(ctx) == null) return;

        var id = Http.Query(ctx, "id");
        if (id == "")
        {
            Http.Html(ctx, PaginaFormulario(ctx, "", "", "CARGA", "", ""));
            return;
        }

        var exercicios = Database.Consultar("""
            SELECT e.nome, e.tipo, eg.id_grupo
            FROM exercicio e
            LEFT JOIN exercicio_grupo eg ON eg.id_exercicio = e.id_exercicio AND eg.papel = 'PRINCIPAL'
            WHERE e.id_exercicio = :id
            """, ("id", int.Parse(id)));

        if (exercicios.Count == 0)
        {
            Http.Redirect(ctx, "/exercicios");
            return;
        }

        var e = exercicios[0];
        Http.Html(ctx, PaginaFormulario(ctx, id,
            e["NOME"]!.ToString()!,
            e["TIPO"]!.ToString()!,
            e["ID_GRUPO"]?.ToString() ?? "",
            ""));
    }

    // POST /exercicios/salvar
    public static void Salvar(HttpListenerContext ctx)
    {
        if (Autenticacao.ExigirAdmin(ctx) == null) return;

        var form = Http.Form(ctx);
        var id = form["id"];
        var nome = form["nome"].Trim();
        var tipo = form["tipo"];
        var idGrupo = form.GetValueOrDefault("id_grupo", "");

        try
        {
            if (id == "")
            {
                // Exercício e grupo principal entram juntos, para o exercício nunca ficar sem principal.
                // O id gerado pelo IDENTITY é encontrado pelo nome, que é UNIQUE.
                Database.Transacao(conn =>
                {
                    Database.Executar(conn,
                        "INSERT INTO exercicio (nome, tipo) VALUES (:nome, :tipo)",
                        ("nome", nome), ("tipo", tipo));

                    Database.Executar(conn, """
                        INSERT INTO exercicio_grupo (id_exercicio, id_grupo, papel)
                        SELECT id_exercicio, :id_grupo, 'PRINCIPAL' FROM exercicio WHERE nome = :nome
                        """,
                        ("id_grupo", int.Parse(idGrupo)), ("nome", nome));
                });
            }
            else
            {
                Database.Executar(
                    "UPDATE exercicio SET nome = :nome, tipo = :tipo WHERE id_exercicio = :id",
                    ("nome", nome), ("tipo", tipo), ("id", int.Parse(id)));
            }

            Http.Redirect(ctx, "/exercicios");
        }
        catch (Exception ex)
        {
            // Na edição o select do grupo está desabilitado e não é enviado; busca o principal de novo
            if (id != "")
                idGrupo = Database.Consultar(
                    "SELECT id_grupo FROM exercicio_grupo WHERE id_exercicio = :id AND papel = 'PRINCIPAL'",
                    ("id", int.Parse(id))).FirstOrDefault()?["ID_GRUPO"]?.ToString() ?? "";

            Http.Html(ctx, PaginaFormulario(ctx, id, nome, tipo, idGrupo, Erros.Traduzir(ex)));
        }
    }

    // POST /exercicios/excluir
    public static void Excluir(HttpListenerContext ctx)
    {
        if (Autenticacao.ExigirAdmin(ctx) == null) return;

        var id = int.Parse(Http.Form(ctx)["id"]);

        try
        {
            // Os grupos do exercício saem antes, porque a FK não tem cascata.
            // Se o exercício estiver em alguma ficha ou série, o segundo DELETE falha (ORA-02292)
            // e o ROLLBACK devolve os grupos.
            Database.Transacao(conn =>
            {
                Database.Executar(conn, "DELETE FROM exercicio_grupo WHERE id_exercicio = :id", ("id", id));
                Database.Executar(conn, "DELETE FROM exercicio WHERE id_exercicio = :id", ("id", id));
            });
            Http.Redirect(ctx, "/exercicios");
        }
        catch (Exception ex)
        {
            Http.Html(ctx, PaginaLista(ctx, Erros.Traduzir(ex)));
        }
    }

    // GET /exercicios/grupos?id=
    public static void Grupos(HttpListenerContext ctx)
    {
        if (Autenticacao.ExigirAdmin(ctx) == null) return;

        var pagina = PaginaGrupos(ctx, int.Parse(Http.Query(ctx, "id")), "");
        if (pagina == null)
            Http.Redirect(ctx, "/exercicios");
        else
            Http.Html(ctx, pagina);
    }

    // POST /exercicios/grupos/incluir
    public static void IncluirGrupo(HttpListenerContext ctx)
    {
        if (Autenticacao.ExigirAdmin(ctx) == null) return;

        var form = Http.Form(ctx);
        var id = int.Parse(form["id"]);

        AlterarGrupos(ctx, id, () => Database.Executar(
            "INSERT INTO exercicio_grupo (id_exercicio, id_grupo, papel) VALUES (:id, :id_grupo, 'SECUNDARIO')",
            ("id", id), ("id_grupo", int.Parse(form["id_grupo"]))));
    }

    // POST /exercicios/grupos/principal
    public static void TornarPrincipal(HttpListenerContext ctx)
    {
        if (Autenticacao.ExigirAdmin(ctx) == null) return;

        var form = Http.Form(ctx);
        var id = int.Parse(form["id"]);

        // A troca precisa ser um UPDATE só: em dois comandos, o gatilho G8 barra o exercício
        // sem principal entre um e outro, ou o índice único barra dois principais.
        AlterarGrupos(ctx, id, () => Database.Executar("""
            UPDATE exercicio_grupo
            SET papel = CASE WHEN id_grupo = :id_grupo THEN 'PRINCIPAL' ELSE 'SECUNDARIO' END
            WHERE id_exercicio = :id
              AND (papel = 'PRINCIPAL' OR id_grupo = :id_grupo)
            """,
            ("id", id), ("id_grupo", int.Parse(form["id_grupo"]))));
    }

    // POST /exercicios/grupos/remover
    public static void RemoverGrupo(HttpListenerContext ctx)
    {
        if (Autenticacao.ExigirAdmin(ctx) == null) return;

        var form = Http.Form(ctx);
        var id = int.Parse(form["id"]);

        AlterarGrupos(ctx, id, () => Database.Executar(
            "DELETE FROM exercicio_grupo WHERE id_exercicio = :id AND id_grupo = :id_grupo",
            ("id", id), ("id_grupo", int.Parse(form["id_grupo"]))));
    }

    // Executa a alteração; se der certo volta para a tela de grupos, se der erro mostra a mensagem nela
    private static void AlterarGrupos(HttpListenerContext ctx, int id, Action alteracao)
    {
        try
        {
            alteracao();
            Http.Redirect(ctx, $"/exercicios/grupos?id={id}");
        }
        catch (Exception ex)
        {
            Http.Html(ctx, PaginaGrupos(ctx, id, Erros.Traduzir(ex)) ?? "");
        }
    }

    // Devolve null se o exercício não existir
    private static string? PaginaGrupos(HttpListenerContext ctx, int id, string erro)
    {
        var exercicios = Database.Consultar("SELECT nome FROM exercicio WHERE id_exercicio = :id", ("id", id));
        if (exercicios.Count == 0) return null;

        var grupos = Database.Consultar("""
            SELECT g.id_grupo, g.nome, eg.papel
            FROM exercicio_grupo eg
            JOIN grupo_muscular g ON g.id_grupo = eg.id_grupo
            WHERE eg.id_exercicio = :id
            ORDER BY CASE eg.papel WHEN 'PRINCIPAL' THEN 0 ELSE 1 END, g.nome
            """, ("id", id));

        var linhas = new StringBuilder();
        foreach (var g in grupos)
        {
            var idGrupo = g["ID_GRUPO"];
            var principal = g["PAPEL"]!.ToString() == "PRINCIPAL";

            linhas.Append("<tr>");
            linhas.Append($"<td>{WebUtility.HtmlEncode(g["NOME"]?.ToString())}</td>");
            linhas.Append(principal
                ? "<td><span class=\"badge text-bg-primary\">Principal</span></td>"
                : "<td><span class=\"badge text-bg-secondary\">Secundário</span></td>");

            // O principal não tem ações: ele só sai quando outro grupo é promovido no lugar dele
            linhas.Append(principal ? "<td></td>" : $"""
                <td class="text-end">
                  <form method="post" action="/exercicios/grupos/principal" class="d-inline">
                    <input type="hidden" name="id" value="{id}">
                    <input type="hidden" name="id_grupo" value="{idGrupo}">
                    <button class="btn btn-sm btn-outline-primary">Tornar principal</button>
                  </form>
                  <form method="post" action="/exercicios/grupos/remover" class="d-inline" onsubmit="return confirm('Remover este grupo do exercício?')">
                    <input type="hidden" name="id" value="{id}">
                    <input type="hidden" name="id_grupo" value="{idGrupo}">
                    <button class="btn btn-sm btn-outline-danger">Remover</button>
                  </form>
                </td>
                """);
            linhas.Append("</tr>");
        }

        // Só os grupos que ainda não estão no exercício
        var opcoes = new StringBuilder();
        foreach (var g in Database.Consultar("""
            SELECT id_grupo, nome FROM grupo_muscular g
            WHERE NOT EXISTS (SELECT 1 FROM exercicio_grupo eg WHERE eg.id_grupo = g.id_grupo AND eg.id_exercicio = :id)
            ORDER BY nome
            """, ("id", id)))
        {
            opcoes.Append($"<option value=\"{g["ID_GRUPO"]}\">{WebUtility.HtmlEncode(g["NOME"]?.ToString())}</option>");
        }

        var nome = exercicios[0]["NOME"]!.ToString()!;
        return Template.Pagina(ctx, "exercicio_grupos.html", new()
        {
            ["EXERCICIO"] = WebUtility.HtmlEncode(nome),
            ["ALERTA"] = Template.Alerta(erro),
            ["ID"] = id.ToString(),
            ["LINHAS"] = linhas.ToString(),
            ["OPCOES_GRUPO"] = opcoes.ToString()
        }, "Grupos de " + nome);
    }

    private static string PaginaLista(HttpListenerContext ctx, string erro)
    {
        var exercicios = Database.Consultar("""
            SELECT e.id_exercicio, e.nome, e.tipo, g.nome AS grupo_principal
            FROM exercicio e
            LEFT JOIN exercicio_grupo eg ON eg.id_exercicio = e.id_exercicio AND eg.papel = 'PRINCIPAL'
            LEFT JOIN grupo_muscular g   ON g.id_grupo      = eg.id_grupo
            ORDER BY e.nome
            """);

        var linhas = new StringBuilder();
        foreach (var e in exercicios)
        {
            var id = e["ID_EXERCICIO"];
            linhas.Append("<tr>");
            linhas.Append($"<td>{WebUtility.HtmlEncode(e["NOME"]?.ToString())}</td>");
            linhas.Append($"<td>{NomeTipo(e["TIPO"]!.ToString()!)}</td>");
            linhas.Append($"<td>{WebUtility.HtmlEncode(e["GRUPO_PRINCIPAL"]?.ToString())}</td>");
            linhas.Append($"""
                <td class="text-end">
                  <a class="btn btn-sm btn-outline-secondary" href="/exercicios/grupos?id={id}">Grupos</a>
                  <a class="btn btn-sm btn-outline-primary" href="/exercicios/form?id={id}">Editar</a>
                  <form method="post" action="/exercicios/excluir" class="d-inline" onsubmit="return confirm('Excluir este exercício?')">
                    <input type="hidden" name="id" value="{id}">
                    <button class="btn btn-sm btn-outline-danger">Excluir</button>
                  </form>
                </td>
                """);
            linhas.Append("</tr>");
        }

        if (exercicios.Count == 0)
            linhas.Append("<tr><td colspan=\"4\" class=\"text-body-secondary\">Nenhum exercício cadastrado.</td></tr>");

        return Template.Pagina(ctx, "exercicios.html", new()
        {
            ["ALERTA"] = Template.Alerta(erro),
            ["LINHAS"] = linhas.ToString()
        }, "Exercícios");
    }

    private static string PaginaFormulario(HttpListenerContext ctx, string id, string nome, string tipo, string idGrupo, string erro)
    {
        var novo = id == "";
        var titulo = novo ? "Novo exercício" : "Editar exercício";

        var opcoes = new StringBuilder();
        foreach (var g in Database.Consultar("SELECT id_grupo, nome FROM grupo_muscular ORDER BY nome"))
        {
            var idOpcao = g["ID_GRUPO"]!.ToString();
            var selecionado = idOpcao == idGrupo ? " selected" : "";
            opcoes.Append($"<option value=\"{idOpcao}\"{selecionado}>{WebUtility.HtmlEncode(g["NOME"]?.ToString())}</option>");
        }

        return Template.Pagina(ctx, "exercicio_form.html", new()
        {
            ["TITULO_FORM"] = titulo,
            ["ALERTA"] = Template.Alerta(erro),
            ["ID"] = WebUtility.HtmlEncode(id),
            ["NOME"] = WebUtility.HtmlEncode(nome),
            ["SEL_CARGA"] = tipo == "CARGA" ? "selected" : "",
            ["SEL_PESO_CORPORAL"] = tipo == "PESO_CORPORAL" ? "selected" : "",
            ["OPCOES_GRUPO"] = opcoes.ToString(),
            // Na edição o principal só aparece; ele é trocado na tela de grupos do exercício
            ["GRUPO_DESABILITADO"] = novo ? "" : "disabled",
            ["AJUDA_GRUPO"] = novo
                ? "Os grupos secundários são incluídos depois, em Grupos."
                : $"Para trocar o grupo principal, use a tela <a href=\"/exercicios/grupos?id={WebUtility.HtmlEncode(id)}\">Grupos</a>."
        }, titulo);
    }

    private static string NomeTipo(string tipo)
    {
        return tipo == "PESO_CORPORAL" ? "Peso corporal" : "Carga";
    }
}

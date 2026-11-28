using FitLog.Infra;
using System.Globalization;
using System.Net;
using System.Text;

namespace FitLog.Handlers;

// Treinos realizados (tabela sessao). Cada usuário só vê e altera os próprios.
public static class SessaoHandler
{
    // GET /treinos
    public static void Listar(HttpListenerContext ctx)
    {
        var usuario = Autenticacao.ExigirLogin(ctx);
        if (usuario == null) return;

        Http.Html(ctx, PaginaLista(ctx, usuario, ""));
    }

    // GET /treinos/form e /treinos/form?id=
    public static void Formulario(HttpListenerContext ctx)
    {
        var usuario = Autenticacao.ExigirLogin(ctx);
        if (usuario == null) return;

        var id = Http.Query(ctx, "id");
        if (id == "")
        {
            Http.Html(ctx, PaginaFormulario(ctx, usuario, "", DateTime.Today.ToString("yyyy-MM-dd"), "", "", "", ""));
            return;
        }

        var sessoes = Database.Consultar("""
            SELECT data, hora_inicio, hora_fim, id_treino
            FROM sessao
            WHERE id_sessao = :id AND id_usuario = :id_usuario
            """, ("id", int.Parse(id)), ("id_usuario", usuario.Id));

        if (sessoes.Count == 0)
        {
            Http.Redirect(ctx, "/treinos");
            return;
        }

        var s = sessoes[0];
        Http.Html(ctx, PaginaFormulario(ctx, usuario, id,
            ((DateTime)s["DATA"]!).ToString("yyyy-MM-dd"),
            ((DateTime)s["HORA_INICIO"]!).ToString("HH:mm"),
            ((DateTime)s["HORA_FIM"]!).ToString("HH:mm"),
            s["ID_TREINO"]?.ToString() ?? "",
            ""));
    }

    // POST /treinos/salvar
    public static void Salvar(HttpListenerContext ctx)
    {
        var usuario = Autenticacao.ExigirLogin(ctx);
        if (usuario == null) return;

        var form = Http.Form(ctx);
        var id = form["id"];
        var data = form["data"];
        var inicio = form["hora_inicio"];
        var fim = form["hora_fim"];
        var idTreino = form["id_treino"];

        try
        {
            // A data vem do <input type="date"> e as horas do <input type="time">;
            // hora_inicio e hora_fim são TIMESTAMP, então juntam a data com a hora
            var dia = DateTime.ParseExact(data, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            var horaInicio = dia + TimeSpan.Parse(inicio, CultureInfo.InvariantCulture);
            var horaFim = dia + TimeSpan.Parse(fim, CultureInfo.InvariantCulture);
            object? treino = idTreino == "" ? null : int.Parse(idTreino);

            if (id == "")
            {
                using var conn = Database.Abrir();
                var novoId = Database.InserirComId(conn, """
                    INSERT INTO sessao (data, hora_inicio, hora_fim, id_usuario, id_treino)
                    VALUES (:data, :hora_inicio, :hora_fim, :id_usuario, :id_treino)
                    """, "id_sessao",
                    ("data", dia), ("hora_inicio", horaInicio), ("hora_fim", horaFim),
                    ("id_usuario", usuario.Id), ("id_treino", treino));

                // Depois de registrar, vai direto para as séries do treino
                Http.Redirect(ctx, $"/treinos/series?id={novoId}");
            }
            else
            {
                Database.Executar("""
                    UPDATE sessao
                    SET data = :data, hora_inicio = :hora_inicio, hora_fim = :hora_fim, id_treino = :id_treino
                    WHERE id_sessao = :id AND id_usuario = :id_usuario
                    """,
                    ("data", dia), ("hora_inicio", horaInicio), ("hora_fim", horaFim),
                    ("id_treino", treino), ("id", int.Parse(id)), ("id_usuario", usuario.Id));

                Http.Redirect(ctx, "/treinos");
            }
        }
        catch (Exception ex)
        {
            Http.Html(ctx, PaginaFormulario(ctx, usuario, id, data, inicio, fim, idTreino, Erros.Traduzir(ex)));
        }
    }

    // POST /treinos/excluir
    public static void Excluir(HttpListenerContext ctx)
    {
        var usuario = Autenticacao.ExigirLogin(ctx);
        if (usuario == null) return;

        try
        {
            // As séries saem em cascata
            Database.Executar(
                "DELETE FROM sessao WHERE id_sessao = :id AND id_usuario = :id_usuario",
                ("id", int.Parse(Http.Form(ctx)["id"])), ("id_usuario", usuario.Id));
            Http.Redirect(ctx, "/treinos");
        }
        catch (Exception ex)
        {
            Http.Html(ctx, PaginaLista(ctx, usuario, Erros.Traduzir(ex)));
        }
    }

    private static string PaginaLista(HttpListenerContext ctx, UsuarioLogado usuario, string erro)
    {
        var sessoes = Database.Consultar("""
            SELECT s.id_sessao, s.data, s.hora_inicio, s.hora_fim, t.nome AS treino,
                   (SELECT COUNT(*) FROM serie se WHERE se.id_sessao = s.id_sessao) AS series
            FROM sessao s
            LEFT JOIN treino t ON t.id_treino = s.id_treino
            WHERE s.id_usuario = :id_usuario
            ORDER BY s.hora_inicio DESC
            """, ("id_usuario", usuario.Id));

        var linhas = new StringBuilder();
        foreach (var s in sessoes)
        {
            var id = s["ID_SESSAO"];
            var inicio = (DateTime)s["HORA_INICIO"]!;
            var fim = (DateTime)s["HORA_FIM"]!;
            var duracao = fim - inicio;
            var treino = s["TREINO"] == null
                ? "<span class=\"text-body-secondary\">Livre</span>"
                : WebUtility.HtmlEncode(s["TREINO"]!.ToString());

            linhas.Append($"""
                <tr>
                  <td>{((DateTime)s["DATA"]!):dd/MM/yyyy}</td>
                  <td>{inicio:HH:mm} – {fim:HH:mm}</td>
                  <td>{(int)duracao.TotalMinutes} min</td>
                  <td>{treino}</td>
                  <td>{s["SERIES"]}</td>
                  <td class="text-end text-nowrap">
                    <a class="btn btn-sm btn-outline-secondary" href="/treinos/series?id={id}">Séries</a>
                    <a class="btn btn-sm btn-outline-primary" href="/treinos/form?id={id}">Editar</a>
                    <form method="post" action="/treinos/excluir" class="d-inline" onsubmit="return confirm('Excluir este treino e todas as séries dele?')">
                      <input type="hidden" name="id" value="{id}">
                      <button class="btn btn-sm btn-outline-danger">Excluir</button>
                    </form>
                  </td>
                </tr>
                """);
        }

        if (sessoes.Count == 0)
            linhas.Append("<tr><td colspan=\"6\" class=\"text-body-secondary\">Nenhum treino registrado.</td></tr>");

        return Template.Pagina(ctx, "treinos.html", new()
        {
            ["ALERTA"] = Template.Alerta(erro),
            ["LINHAS"] = linhas.ToString()
        }, "Treinos realizados");
    }

    private static string PaginaFormulario(HttpListenerContext ctx, UsuarioLogado usuario, string id,
                                           string data, string inicio, string fim, string idTreino, string erro)
    {
        var novo = id == "";
        var titulo = novo ? "Registrar treino" : "Editar treino";

        return Template.Pagina(ctx, "treino_form.html", new()
        {
            ["TITULO_FORM"] = titulo,
            ["ALERTA"] = Template.Alerta(erro),
            ["ID"] = WebUtility.HtmlEncode(id),
            ["DATA"] = WebUtility.HtmlEncode(data),
            ["HORA_INICIO"] = WebUtility.HtmlEncode(inicio),
            ["HORA_FIM"] = WebUtility.HtmlEncode(fim),
            ["OPCOES_TREINO"] = OpcoesTreino(usuario, idTreino),
            ["BOTAO"] = novo ? "Registrar e incluir séries" : "Salvar"
        }, titulo);
    }

    // Treinos que podem ser escolhidos: os do plano ativo e os avulsos do usuário.
    // Na edição, o treino que já está gravado também aparece, mesmo que seja de um plano que não está mais ativo.
    private static string OpcoesTreino(UsuarioLogado usuario, string idTreino)
    {
        var html = new StringBuilder();
        var listados = new HashSet<string>();

        string Opcao(Dictionary<string, object?> t)
        {
            var id = t["ID_TREINO"]!.ToString()!;
            listados.Add(id);
            var selecionado = id == idTreino ? " selected" : "";
            return $"<option value=\"{id}\"{selecionado}>{WebUtility.HtmlEncode(t["NOME"]?.ToString())}</option>";
        }

        var doPlano = Database.Consultar("""
            SELECT t.id_treino, t.nome, p.nome AS plano
            FROM treino t
            JOIN usuario_plano up ON up.id_plano = t.id_plano AND up.id_usuario = :id_usuario AND up.ativo = 1
            JOIN plano p ON p.id_plano = t.id_plano
            ORDER BY t.ordem
            """, ("id_usuario", usuario.Id));
        if (doPlano.Count > 0)
        {
            html.Append($"<optgroup label=\"Plano ativo: {WebUtility.HtmlEncode(doPlano[0]["PLANO"]?.ToString())}\">");
            foreach (var t in doPlano) html.Append(Opcao(t));
            html.Append("</optgroup>");
        }

        var avulsos = Database.Consultar(
            "SELECT id_treino, nome FROM treino WHERE tipo_treino = 'AVULSO' AND id_usuario = :id_usuario ORDER BY nome",
            ("id_usuario", usuario.Id));
        if (avulsos.Count > 0)
        {
            html.Append("<optgroup label=\"Treinos avulsos\">");
            foreach (var t in avulsos) html.Append(Opcao(t));
            html.Append("</optgroup>");
        }

        if (idTreino != "" && !listados.Contains(idTreino))
        {
            var atual = Database.Consultar("SELECT id_treino, nome FROM treino WHERE id_treino = :id", ("id", int.Parse(idTreino)));
            if (atual.Count > 0)
                html.Append($"<optgroup label=\"Treino gravado\">{Opcao(atual[0])}</optgroup>");
        }

        return html.ToString();
    }
}

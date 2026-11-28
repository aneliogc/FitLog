using FitLog.Infra;
using System.Globalization;
using System.Net;
using System.Text;

namespace FitLog.Handlers;

// Séries de um treino realizado (tabela serie, entidade fraca de sessao).
public static class SerieHandler
{
    private static readonly CultureInfo Brasil = new("pt-BR");

    // GET /treinos/series?id=                         (lista e formulário de nova série)
    // GET /treinos/series?id=&ultimo=                 (depois de incluir: repete o último exercício)
    // GET /treinos/series?id=&exercicio=&numero=      (formulário para editar uma série)
    public static void Listar(HttpListenerContext ctx)
    {
        var usuario = Autenticacao.ExigirLogin(ctx);
        if (usuario == null) return;

        var idSessao = int.Parse(Http.Query(ctx, "id"));
        if (!DoUsuario(idSessao, usuario))
        {
            Http.Redirect(ctx, "/treinos");
            return;
        }

        var exercicio = Http.Query(ctx, "exercicio");
        var numero = Http.Query(ctx, "numero");
        var ultimo = Http.Query(ctx, "ultimo");

        var valores = new Valores();
        if (exercicio != "" && numero != "")
        {
            // Editar: carrega a série escolhida
            valores = Carregar(idSessao, int.Parse(exercicio), "AND numero = :numero", int.Parse(numero)) ?? new Valores();
            if (valores.IdExercicio == "") numero = "";
        }
        else if (ultimo != "")
        {
            // Nova série depois de incluir uma: já vem com o mesmo exercício e os valores da última série dele
            valores = Carregar(idSessao, int.Parse(ultimo), "ORDER BY numero DESC FETCH FIRST 1 ROW ONLY", null) ?? new Valores();
        }

        Http.Html(ctx, Pagina(ctx, idSessao, numero, valores, ""));
    }

    // POST /treinos/series/salvar
    public static void Salvar(HttpListenerContext ctx)
    {
        var usuario = Autenticacao.ExigirLogin(ctx);
        if (usuario == null) return;

        var form = Http.Form(ctx);
        var idSessao = int.Parse(form["id_sessao"]);
        var numero = form["numero"];
        var valores = new Valores
        {
            IdExercicio = form["id_exercicio"],
            Carga = form["carga"],
            Repeticoes = form["repeticoes"],
            Descanso = form["descanso"]
        };

        if (!DoUsuario(idSessao, usuario))
        {
            Http.Redirect(ctx, "/treinos");
            return;
        }

        try
        {
            // Aceita vírgula ou ponto na carga (ex.: 12,5 ou 12.5)
            var carga = decimal.Parse(valores.Carga.Replace(',', '.'), CultureInfo.InvariantCulture);
            var repeticoes = int.Parse(valores.Repeticoes);
            object? descanso = valores.Descanso == "" ? null : int.Parse(valores.Descanso);

            if (numero == "")
            {
                // Uma série por vez e sem o número:
                Database.Executar("""
                    INSERT INTO serie (id_sessao, id_exercicio, carga, repeticoes, descanso)
                    VALUES (:id_sessao, :id_exercicio, :carga, :repeticoes, :descanso)
                    """,
                    ("id_sessao", idSessao), ("id_exercicio", int.Parse(valores.IdExercicio)),
                    ("carga", carga), ("repeticoes", repeticoes), ("descanso", descanso));

                Http.Redirect(ctx, $"/treinos/series?id={idSessao}&ultimo={valores.IdExercicio}");
            }
            else
            {
                Database.Executar("""
                    UPDATE serie
                    SET carga = :carga, repeticoes = :repeticoes, descanso = :descanso
                    WHERE id_sessao = :id_sessao AND id_exercicio = :id_exercicio AND numero = :numero
                    """,
                    ("carga", carga), ("repeticoes", repeticoes), ("descanso", descanso),
                    ("id_sessao", idSessao), ("id_exercicio", int.Parse(valores.IdExercicio)), ("numero", int.Parse(numero)));

                Http.Redirect(ctx, $"/treinos/series?id={idSessao}");
            }
        }
        catch (Exception ex)
        {
            Http.Html(ctx, Pagina(ctx, idSessao, numero, valores, Erros.Traduzir(ex)));
        }
    }

    // POST /treinos/series/excluir
    public static void Excluir(HttpListenerContext ctx)
    {
        var usuario = Autenticacao.ExigirLogin(ctx);
        if (usuario == null) return;

        var form = Http.Form(ctx);
        var idSessao = int.Parse(form["id_sessao"]);

        if (!DoUsuario(idSessao, usuario))
        {
            Http.Redirect(ctx, "/treinos");
            return;
        }

        try
        {
            Database.Executar(
                "DELETE FROM serie WHERE id_sessao = :id_sessao AND id_exercicio = :id_exercicio AND numero = :numero",
                ("id_sessao", idSessao), ("id_exercicio", int.Parse(form["id_exercicio"])), ("numero", int.Parse(form["numero"])));
            Http.Redirect(ctx, $"/treinos/series?id={idSessao}");
        }
        catch (Exception ex)
        {
            Http.Html(ctx, Pagina(ctx, idSessao, "", new Valores(), Erros.Traduzir(ex)));
        }
    }

    // Valores do formulário, como texto
    private class Valores
    {
        public string IdExercicio = "";
        public string Carga = "";
        public string Repeticoes = "";
        public string Descanso = "";
    }

    private static bool DoUsuario(int idSessao, UsuarioLogado usuario)
    {
        return Database.Consultar(
            "SELECT 1 FROM sessao WHERE id_sessao = :id AND id_usuario = :id_usuario",
            ("id", idSessao), ("id_usuario", usuario.Id)).Count > 0;
    }

    private static Valores? Carregar(int idSessao, int idExercicio, string complemento, int? numero)
    {
        var parametros = new List<(string, object?)> { ("id_sessao", idSessao), ("id_exercicio", idExercicio) };
        if (numero != null) parametros.Add(("numero", numero));

        var series = Database.Consultar(
            "SELECT id_exercicio, carga, repeticoes, descanso FROM serie WHERE id_sessao = :id_sessao AND id_exercicio = :id_exercicio " + complemento,
            parametros.ToArray());
        if (series.Count == 0) return null;

        var s = series[0];
        return new Valores
        {
            IdExercicio = s["ID_EXERCICIO"]!.ToString()!,
            Carga = Convert.ToDecimal(s["CARGA"]).ToString("0.##", CultureInfo.InvariantCulture),
            Repeticoes = s["REPETICOES"]!.ToString()!,
            Descanso = s["DESCANSO"]?.ToString() ?? ""
        };
    }

    private static string Pagina(HttpListenerContext ctx, int idSessao, string numero, Valores valores, string erro)
    {
        var sessao = Database.Consultar("""
            SELECT s.data, s.hora_inicio, s.hora_fim, s.id_treino, t.nome AS treino
            FROM sessao s
            LEFT JOIN treino t ON t.id_treino = s.id_treino
            WHERE s.id_sessao = :id
            """, ("id", idSessao))[0];

        // As séries aparecem na ordem dos exercícios no treino (os de fora do treino vão para o fim)
        var series = Database.Consultar("""
            SELECT se.id_exercicio, e.nome, e.tipo, se.numero, se.carga, se.repeticoes, se.descanso
            FROM serie se
            JOIN exercicio e ON e.id_exercicio = se.id_exercicio
            LEFT JOIN treino_exercicio te ON te.id_treino = :id_treino AND te.id_exercicio = se.id_exercicio
            WHERE se.id_sessao = :id_sessao
            ORDER BY NVL(te.ordem_exerc, 9999), e.nome, se.numero
            """, ("id_treino", sessao["ID_TREINO"]), ("id_sessao", idSessao));

        var linhas = new StringBuilder();
        string? exercicioAnterior = null;
        foreach (var s in series)
        {
            var idExercicio = s["ID_EXERCICIO"]!.ToString();
            var num = s["NUMERO"];
            // O nome do exercício só aparece na primeira série dele
            var nome = idExercicio == exercicioAnterior ? "" : WebUtility.HtmlEncode(s["NOME"]?.ToString());
            exercicioAnterior = idExercicio;

            var carga = s["TIPO"]!.ToString() == "PESO_CORPORAL"
                ? "<span class=\"text-body-secondary\">-</span>"
                : Convert.ToDecimal(s["CARGA"]).ToString("0.##", Brasil) + " kg";
            var descanso = s["DESCANSO"] == null ? "" : s["DESCANSO"] + " s";

            linhas.Append($"""
                <tr>
                  <td><strong>{nome}</strong></td>
                  <td>{num}</td>
                  <td>{carga}</td>
                  <td>{s["REPETICOES"]}</td>
                  <td>{descanso}</td>
                  <td class="text-end text-nowrap">
                    <a class="btn btn-sm btn-outline-primary" href="/treinos/series?id={idSessao}&exercicio={idExercicio}&numero={num}">Editar</a>
                    <form method="post" action="/treinos/series/excluir" class="d-inline" onsubmit="return confirm('Excluir esta série?')">
                      <input type="hidden" name="id_sessao" value="{idSessao}">
                      <input type="hidden" name="id_exercicio" value="{idExercicio}">
                      <input type="hidden" name="numero" value="{num}">
                      <button class="btn btn-sm btn-outline-danger">Excluir</button>
                    </form>
                  </td>
                </tr>
                """);
        }

        if (series.Count == 0)
            linhas.Append("<tr><td colspan=\"6\" class=\"text-body-secondary\">Nenhuma série registrada.</td></tr>");

        var editando = numero != "";
        var treino = sessao["TREINO"] == null ? "Treino livre" : sessao["TREINO"]!.ToString()!;
        var data = ((DateTime)sessao["DATA"]!).ToString("dd/MM/yyyy");
        var horario = $"{(DateTime)sessao["HORA_INICIO"]!:HH:mm} – {(DateTime)sessao["HORA_FIM"]!:HH:mm}";

        return Template.Pagina(ctx, "treino_series.html", new()
        {
            ["TREINO"] = WebUtility.HtmlEncode(treino),
            ["QUANDO"] = $"{data}, {horario}",
            ["ALERTA"] = Template.Alerta(erro),
            ["LINHAS"] = linhas.ToString(),
            ["TITULO_FORM"] = editando ? $"Editar série {WebUtility.HtmlEncode(numero)}" : "Nova série",
            ["ID_SESSAO"] = idSessao.ToString(),
            ["NUMERO"] = WebUtility.HtmlEncode(numero),
            // Na edição o exercício não muda (ele faz parte da chave da série): o select fica desabilitado
            // e o valor vai num campo escondido
            ["EXERCICIO_DESABILITADO"] = editando ? "disabled" : "",
            ["ID_EXERCICIO_FIXO"] = editando ? $"""<input type="hidden" name="id_exercicio" value="{WebUtility.HtmlEncode(valores.IdExercicio)}">""" : "",
            ["OPCOES_EXERCICIO"] = OpcoesExercicio(sessao["ID_TREINO"], treino, valores.IdExercicio),
            ["CARGA"] = WebUtility.HtmlEncode(valores.Carga),
            ["REPETICOES"] = WebUtility.HtmlEncode(valores.Repeticoes),
            ["DESCANSO"] = WebUtility.HtmlEncode(valores.Descanso),
            ["CANCELAR"] = editando ? $"""<a class="btn btn-secondary" href="/treinos/series?id={idSessao}">Cancelar</a>""" : ""
        }, treino + " – séries");
    }

    // Primeiro um grupo com o nome do treino e os exercícios dele (na ordem do treino),
    // depois "Outros exercícios", para quando a pessoa troca um exercício na hora (ex.: aparelho ocupado).
    // Treino livre: todos os exercícios, sem grupos.
    private static string OpcoesExercicio(object? idTreino, string nomeTreino, string idExercicio)
    {
        // LEFT JOIN: ordem_exerc só vem preenchida para os exercícios que estão no treino
        var exercicios = Database.Consultar("""
            SELECT e.id_exercicio, e.nome, te.ordem_exerc
            FROM exercicio e
            LEFT JOIN treino_exercicio te ON te.id_exercicio = e.id_exercicio AND te.id_treino = :id_treino
            ORDER BY NVL(te.ordem_exerc, 9999), e.nome
            """, ("id_treino", idTreino));

        var doTreino = new StringBuilder();
        var outros = new StringBuilder();
        foreach (var e in exercicios)
        {
            var id = e["ID_EXERCICIO"]!.ToString();
            var selecionado = id == idExercicio ? " selected" : "";
            var opcao = $"<option value=\"{id}\"{selecionado}>{WebUtility.HtmlEncode(e["NOME"]?.ToString())}</option>";
            if (e["ORDEM_EXERC"] != null) doTreino.Append(opcao);
            else outros.Append(opcao);
        }

        if (doTreino.Length == 0) return outros.ToString();
        return $"<optgroup label=\"{WebUtility.HtmlEncode(nomeTreino)}\">{doTreino}</optgroup>"
             + $"<optgroup label=\"Outros exercícios\">{outros}</optgroup>";
    }
}

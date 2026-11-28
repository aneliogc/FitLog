using FitLog.Infra;
using System.Net;
using System.Text;

namespace FitLog.Handlers;

// Exercícios de uma ficha (tabela treino_exercicio). Serve a qualquer ficha que o usuário
// possa alterar: de plano predefinido (admin), de plano personalizado ou avulsa (dono).
public static class TreinoExercicioHandler
{
    // GET /fichas/exercicios?id=
    public static void Listar(HttpListenerContext ctx)
    {
        var usuario = Autenticacao.ExigirLogin(ctx);
        if (usuario == null) return;

        var idTreino = int.Parse(Http.Query(ctx, "id"));
        if (!Autenticacao.PodeAlterarFicha(usuario, idTreino))
        {
            Autenticacao.NegarAcesso(ctx);
            return;
        }

        Http.Html(ctx, Pagina(ctx, idTreino, ""));
    }

    // POST /fichas/exercicios/incluir
    public static void Incluir(HttpListenerContext ctx)
    {
        Alterar(ctx, (form, idTreino) => Database.Executar("""
            INSERT INTO treino_exercicio (id_treino, id_exercicio, ordem_exerc)
            SELECT :id_treino, :id_exercicio, NVL(MAX(ordem_exerc), 0) + 1
            FROM treino_exercicio WHERE id_treino = :id_treino
            """,
            ("id_treino", idTreino), ("id_exercicio", int.Parse(form["id_exercicio"]))));
    }

    // POST /fichas/exercicios/remover
    public static void Remover(HttpListenerContext ctx)
    {
        Alterar(ctx, (form, idTreino) => Database.Executar(
            "DELETE FROM treino_exercicio WHERE id_treino = :id_treino AND id_exercicio = :id_exercicio",
            ("id_treino", idTreino), ("id_exercicio", int.Parse(form["id_exercicio"]))));
    }

    // POST /fichas/exercicios/mover
    public static void Mover(HttpListenerContext ctx)
    {
        Alterar(ctx, (form, idTreino) =>
        {
            var atual = Database.Consultar(
                "SELECT ordem_exerc FROM treino_exercicio WHERE id_treino = :id_treino AND id_exercicio = :id_exercicio",
                ("id_treino", idTreino), ("id_exercicio", int.Parse(form["id_exercicio"])));
            if (atual.Count == 0) return;

            var ordem = atual[0]["ORDEM_EXERC"];
            var sqlVizinho = form["direcao"] == "cima"
                ? "SELECT MAX(ordem_exerc) AS ordem FROM treino_exercicio WHERE id_treino = :id_treino AND ordem_exerc < :ordem"
                : "SELECT MIN(ordem_exerc) AS ordem FROM treino_exercicio WHERE id_treino = :id_treino AND ordem_exerc > :ordem";
            var vizinho = Database.Consultar(sqlVizinho, ("id_treino", idTreino), ("ordem", ordem))[0]["ORDEM"];
            if (vizinho == null) return;

            // Troca num UPDATE só, por causa da restrição UNIQUE (id_treino, ordem_exerc)
            Database.Executar("""
                UPDATE treino_exercicio
                SET ordem_exerc = CASE WHEN ordem_exerc = :a THEN :b ELSE :a END
                WHERE id_treino = :id_treino AND ordem_exerc IN (:a, :b)
                """,
                ("a", ordem), ("b", vizinho), ("id_treino", idTreino));
        });
    }

    // Confere login e permissão, executa a alteração e volta para a ficha (ou mostra o erro nela)
    private static void Alterar(HttpListenerContext ctx, Action<Dictionary<string, string>, int> alteracao)
    {
        var usuario = Autenticacao.ExigirLogin(ctx);
        if (usuario == null) return;

        var form = Http.Form(ctx);
        var idTreino = int.Parse(form["id_treino"]);

        if (!Autenticacao.PodeAlterarFicha(usuario, idTreino))
        {
            Autenticacao.NegarAcesso(ctx);
            return;
        }

        try
        {
            alteracao(form, idTreino);
            Http.Redirect(ctx, $"/fichas/exercicios?id={idTreino}");
        }
        catch (Exception ex)
        {
            Http.Html(ctx, Pagina(ctx, idTreino, Erros.Traduzir(ex)));
        }
    }

    private static string Pagina(HttpListenerContext ctx, int idTreino, string erro)
    {
        var ficha = Database.Consultar("""
            SELECT t.nome, t.tipo_treino, t.id_plano, p.tipo AS tipo_plano
            FROM treino t
            LEFT JOIN plano p ON p.id_plano = t.id_plano
            WHERE t.id_treino = :id
            """, ("id", idTreino))[0];

        var voltar = ficha["TIPO_TREINO"]!.ToString() == "AVULSO"
            ? """<a href="/meus-planos" class="text-decoration-none">&larr; Meus planos</a>"""
            : $"""<a href="/planos/fichas?id={ficha["ID_PLANO"]}" class="text-decoration-none">&larr; Treinos do plano</a>""";

        var exercicios = Database.Consultar("""
            SELECT e.id_exercicio, e.nome, e.tipo
            FROM treino_exercicio te
            JOIN exercicio e ON e.id_exercicio = te.id_exercicio
            WHERE te.id_treino = :id
            ORDER BY te.ordem_exerc
            """, ("id", idTreino));

        var linhas = new StringBuilder();
        for (int i = 0; i < exercicios.Count; i++)
        {
            var e = exercicios[i];
            var id = e["ID_EXERCICIO"];
            var cima = i == 0 ? "disabled" : "";
            var baixo = i == exercicios.Count - 1 ? "disabled" : "";
            var tipo = e["TIPO"]!.ToString() == "PESO_CORPORAL" ? "Peso corporal" : "Carga";

            linhas.Append("<tr>");
            linhas.Append($"<td>{i + 1}</td>");
            linhas.Append($"<td>{WebUtility.HtmlEncode(e["NOME"]?.ToString())}</td>");
            linhas.Append($"<td>{tipo}</td>");
            linhas.Append($"""
                <td class="text-end text-nowrap">
                  <form method="post" action="/fichas/exercicios/mover" class="d-inline">
                    <input type="hidden" name="id_treino" value="{idTreino}">
                    <input type="hidden" name="id_exercicio" value="{id}">
                    <button class="btn btn-sm btn-outline-secondary" name="direcao" value="cima" title="Subir" {cima}>&uarr;</button>
                    <button class="btn btn-sm btn-outline-secondary" name="direcao" value="baixo" title="Descer" {baixo}>&darr;</button>
                  </form>
                  <form method="post" action="/fichas/exercicios/remover" class="d-inline" onsubmit="return confirm('Remover este exercício do treino?')">
                    <input type="hidden" name="id_treino" value="{idTreino}">
                    <input type="hidden" name="id_exercicio" value="{id}">
                    <button class="btn btn-sm btn-outline-danger">Remover</button>
                  </form>
                </td>
                """);
            linhas.Append("</tr>");
        }

        if (exercicios.Count == 0)
            linhas.Append("<tr><td colspan=\"4\" class=\"text-body-secondary\">Nenhum exercício neste treino.</td></tr>");

        // Só os exercícios que ainda não estão na ficha
        var opcoes = new StringBuilder();
        foreach (var e in Database.Consultar("""
            SELECT id_exercicio, nome FROM exercicio e
            WHERE NOT EXISTS (SELECT 1 FROM treino_exercicio te WHERE te.id_exercicio = e.id_exercicio AND te.id_treino = :id)
            ORDER BY nome
            """, ("id", idTreino)))
        {
            opcoes.Append($"<option value=\"{e["ID_EXERCICIO"]}\">{WebUtility.HtmlEncode(e["NOME"]?.ToString())}</option>");
        }

        var nome = ficha["NOME"]!.ToString()!;
        return Template.Pagina(ctx, "ficha_exercicios.html", new()
        {
            ["VOLTAR"] = voltar,
            ["FICHA"] = WebUtility.HtmlEncode(nome),
            ["ALERTA"] = Template.Alerta(erro),
            ["LINHAS"] = linhas.ToString(),
            ["ID_TREINO"] = idTreino.ToString(),
            ["OPCOES_EXERCICIO"] = opcoes.ToString()
        }, nome);
    }
}

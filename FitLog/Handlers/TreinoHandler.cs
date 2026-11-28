using FitLog.Infra;
using System.Net;
using System.Text;

namespace FitLog.Handlers;

// Fichas (tabela treino). Aqui ficam as fichas de um plano; serve ao admin (planos predefinidos)
// e ao dono de um plano personalizado, conforme Autenticacao.PodeAlterarPlano.
public static class TreinoHandler
{
    // GET /planos/fichas?id=          (lista e formulário de nova ficha)
    // GET /planos/fichas?id=&ficha=   (lista e formulário para renomear a ficha)
    public static void FichasDoPlano(HttpListenerContext ctx)
    {
        var usuario = Autenticacao.ExigirLogin(ctx);
        if (usuario == null) return;

        var idPlano = int.Parse(Http.Query(ctx, "id"));
        if (!Autenticacao.PodeAlterarPlano(usuario, idPlano))
        {
            Autenticacao.NegarAcesso(ctx);
            return;
        }

        var ficha = Http.Query(ctx, "ficha");
        var nome = "";
        if (ficha != "")
        {
            var fichas = Database.Consultar(
                "SELECT nome FROM treino WHERE id_treino = :id AND id_plano = :id_plano",
                ("id", int.Parse(ficha)), ("id_plano", idPlano));
            if (fichas.Count == 0) ficha = "";
            else nome = fichas[0]["NOME"]!.ToString()!;
        }

        Http.Html(ctx, PaginaFichas(ctx, idPlano, ficha, nome, ""));
    }

    // POST /planos/fichas/salvar
    public static void SalvarFicha(HttpListenerContext ctx)
    {
        var usuario = Autenticacao.ExigirLogin(ctx);
        if (usuario == null) return;

        var form = Http.Form(ctx);
        var idPlano = int.Parse(form["id_plano"]);
        var idTreino = form["id_treino"];
        var nome = form["nome"].Trim();

        if (!Autenticacao.PodeAlterarPlano(usuario, idPlano))
        {
            Autenticacao.NegarAcesso(ctx);
            return;
        }

        try
        {
            if (idTreino == "")
            {
                // A nova ficha entra no fim do plano: ordem = maior ordem + 1
                Database.Executar("""
                    INSERT INTO treino (nome, tipo_treino, id_plano, ordem)
                    SELECT :nome, 'PLANO', :id_plano, NVL(MAX(ordem), 0) + 1
                    FROM treino WHERE id_plano = :id_plano
                    """,
                    ("nome", nome), ("id_plano", idPlano));
            }
            else
            {
                Database.Executar(
                    "UPDATE treino SET nome = :nome WHERE id_treino = :id AND id_plano = :id_plano",
                    ("nome", nome), ("id", int.Parse(idTreino)), ("id_plano", idPlano));
            }

            Http.Redirect(ctx, $"/planos/fichas?id={idPlano}");
        }
        catch (Exception ex)
        {
            Http.Html(ctx, PaginaFichas(ctx, idPlano, idTreino, nome, Erros.Traduzir(ex)));
        }
    }

    // POST /planos/fichas/excluir
    public static void ExcluirFicha(HttpListenerContext ctx)
    {
        var usuario = Autenticacao.ExigirLogin(ctx);
        if (usuario == null) return;

        var form = Http.Form(ctx);
        var idPlano = int.Parse(form["id_plano"]);

        if (!Autenticacao.PodeAlterarPlano(usuario, idPlano))
        {
            Autenticacao.NegarAcesso(ctx);
            return;
        }

        try
        {
            // Os exercícios da ficha saem em cascata; os treinos realizados com ela ficam sem ficha (SET NULL)
            Database.Executar(
                "DELETE FROM treino WHERE id_treino = :id AND id_plano = :id_plano",
                ("id", int.Parse(form["id_treino"])), ("id_plano", idPlano));
            Http.Redirect(ctx, $"/planos/fichas?id={idPlano}");
        }
        catch (Exception ex)
        {
            Http.Html(ctx, PaginaFichas(ctx, idPlano, "", "", Erros.Traduzir(ex)));
        }
    }

    // POST /planos/fichas/mover
    public static void MoverFicha(HttpListenerContext ctx)
    {
        var usuario = Autenticacao.ExigirLogin(ctx);
        if (usuario == null) return;

        var form = Http.Form(ctx);
        var idPlano = int.Parse(form["id_plano"]);

        if (!Autenticacao.PodeAlterarPlano(usuario, idPlano))
        {
            Autenticacao.NegarAcesso(ctx);
            return;
        }

        var atual = Database.Consultar(
            "SELECT ordem FROM treino WHERE id_treino = :id AND id_plano = :id_plano",
            ("id", int.Parse(form["id_treino"])), ("id_plano", idPlano));

        if (atual.Count > 0)
        {
            var ordem = atual[0]["ORDEM"];

            // A vizinha é a ficha com a maior ordem abaixo (subir) ou a menor ordem acima (descer)
            var sqlVizinha = form["direcao"] == "cima"
                ? "SELECT MAX(ordem) AS ordem FROM treino WHERE id_plano = :id_plano AND ordem < :ordem"
                : "SELECT MIN(ordem) AS ordem FROM treino WHERE id_plano = :id_plano AND ordem > :ordem";
            var vizinha = Database.Consultar(sqlVizinha, ("id_plano", idPlano), ("ordem", ordem))[0]["ORDEM"];

            if (vizinha != null)
            {
                // Troca as duas ordens num UPDATE só, para a restrição UNIQUE (id_plano, ordem)
                // não ver duas fichas na mesma posição no meio da troca
                Database.Executar("""
                    UPDATE treino
                    SET ordem = CASE WHEN ordem = :a THEN :b ELSE :a END
                    WHERE id_plano = :id_plano AND ordem IN (:a, :b)
                    """,
                    ("a", ordem), ("b", vizinha), ("id_plano", idPlano));
            }
        }

        Http.Redirect(ctx, $"/planos/fichas?id={idPlano}");
    }

    private static string PaginaFichas(HttpListenerContext ctx, int idPlano, string idTreino, string nome, string erro)
    {
        // dono: o usuário do plano personalizado (nulo no predefinido)
        var plano = Database.Consultar("""
            SELECT p.nome, p.tipo,
                   (SELECT MIN(up.id_usuario) FROM usuario_plano up WHERE up.id_plano = p.id_plano) AS dono
            FROM plano p
            WHERE p.id_plano = :id
            """, ("id", idPlano))[0];

        // O link de volta depende de quem está vendo: o dono volta para Meus planos;
        // o admin volta para os planos predefinidos ou para os planos do usuário dono
        string voltar;
        if (plano["TIPO"]!.ToString() == "PREDEFINIDO")
            voltar = """<a href="/planos" class="text-decoration-none">&larr; Planos predefinidos</a>""";
        else if (Convert.ToInt32(plano["DONO"]) == Autenticacao.Atual(ctx)!.Id)
            voltar = """<a href="/meus-planos" class="text-decoration-none">&larr; Meus planos</a>""";
        else
            voltar = $"""<a href="/usuarios/planos?id={plano["DONO"]}" class="text-decoration-none">&larr; Planos do usuário</a>""";

        var fichas = Database.Consultar("""
            SELECT t.id_treino, t.nome,
                   (SELECT COUNT(*) FROM treino_exercicio te WHERE te.id_treino = t.id_treino) AS exercicios
            FROM treino t
            WHERE t.id_plano = :id
            ORDER BY t.ordem
            """, ("id", idPlano));

        var linhas = new StringBuilder();
        for (int i = 0; i < fichas.Count; i++)
        {
            var f = fichas[i];
            var id = f["ID_TREINO"];
            var cima = i == 0 ? "disabled" : "";
            var baixo = i == fichas.Count - 1 ? "disabled" : "";

            linhas.Append("<tr>");
            linhas.Append($"<td>{i + 1}</td>");
            linhas.Append($"<td>{WebUtility.HtmlEncode(f["NOME"]?.ToString())}</td>");
            linhas.Append($"<td>{f["EXERCICIOS"]}</td>");
            linhas.Append($"""
                <td class="text-end text-nowrap">
                  <form method="post" action="/planos/fichas/mover" class="d-inline">
                    <input type="hidden" name="id_plano" value="{idPlano}">
                    <input type="hidden" name="id_treino" value="{id}">
                    <button class="btn btn-sm btn-outline-secondary" name="direcao" value="cima" title="Subir" {cima}>&uarr;</button>
                    <button class="btn btn-sm btn-outline-secondary" name="direcao" value="baixo" title="Descer" {baixo}>&darr;</button>
                  </form>
                  <a class="btn btn-sm btn-outline-secondary" href="/fichas/exercicios?id={id}">Exercícios</a>
                  <a class="btn btn-sm btn-outline-primary" href="/planos/fichas?id={idPlano}&ficha={id}">Renomear</a>
                  <form method="post" action="/planos/fichas/excluir" class="d-inline" onsubmit="return confirm('Excluir este treino?')">
                    <input type="hidden" name="id_plano" value="{idPlano}">
                    <input type="hidden" name="id_treino" value="{id}">
                    <button class="btn btn-sm btn-outline-danger">Excluir</button>
                  </form>
                </td>
                """);
            linhas.Append("</tr>");
        }

        if (fichas.Count == 0)
            linhas.Append("<tr><td colspan=\"4\" class=\"text-body-secondary\">Nenhum treino neste plano.</td></tr>");

        var editando = idTreino != "";
        return Template.Pagina(ctx, "plano_fichas.html", new()
        {
            ["VOLTAR"] = voltar,
            ["PLANO"] = WebUtility.HtmlEncode(plano["NOME"]!.ToString()!),
            ["ALERTA"] = Template.Alerta(erro),
            ["LINHAS"] = linhas.ToString(),
            ["ID_PLANO"] = idPlano.ToString(),
            ["ID_TREINO"] = WebUtility.HtmlEncode(idTreino),
            ["NOME"] = WebUtility.HtmlEncode(nome),
            ["TITULO_FORM"] = editando ? "Renomear treino" : "Novo treino",
            ["CANCELAR"] = editando ? $"""<a class="btn btn-secondary" href="/planos/fichas?id={idPlano}">Cancelar</a>""" : ""
        }, "Treinos de " + plano["NOME"]);
    }
}

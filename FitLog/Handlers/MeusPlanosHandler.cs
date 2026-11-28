using FitLog.Infra;
using System.Net;
using System.Text;

namespace FitLog.Handlers;

// Meus planos: todos os planos predefinidos, os planos personalizados do usuário e as fichas avulsas dele.
// O usuário escolhe um plano ativo; as fichas dele e as avulsas são as que aparecem ao registrar um treino.
// A linha em usuario_plano de um predefinido é criada na primeira ativação.
// Todas as consultas e alterações filtram pelo usuário logado (regra 15).
public static class MeusPlanosHandler
{
    // GET /meus-planos
    public static void Exibir(HttpListenerContext ctx)
    {
        var usuario = Autenticacao.ExigirLogin(ctx);
        if (usuario == null) return;

        Http.Html(ctx, Pagina(ctx, usuario, ""));
    }

    // GET /meus-planos/form e /meus-planos/form?id=   (novo plano personalizado ou renomear)
    public static void FormularioPersonalizado(HttpListenerContext ctx)
    {
        var usuario = Autenticacao.ExigirLogin(ctx);
        if (usuario == null) return;

        var id = Http.Query(ctx, "id");
        if (id == "")
        {
            Http.Html(ctx, PaginaFormulario(ctx, "", "", ""));
            return;
        }

        var planos = Database.Consultar("""
            SELECT p.nome FROM plano p
            JOIN usuario_plano up ON up.id_plano = p.id_plano
            WHERE p.id_plano = :id AND p.tipo = 'PERSONALIZADO' AND up.id_usuario = :id_usuario
            """, ("id", int.Parse(id)), ("id_usuario", usuario.Id));

        if (planos.Count == 0)
        {
            Http.Redirect(ctx, "/meus-planos");
            return;
        }

        Http.Html(ctx, PaginaFormulario(ctx, id, planos[0]["NOME"]!.ToString()!, ""));
    }

    // POST /meus-planos/salvar   (cria ou renomeia um plano personalizado)
    public static void SalvarPersonalizado(HttpListenerContext ctx)
    {
        var usuario = Autenticacao.ExigirLogin(ctx);
        if (usuario == null) return;

        var form = Http.Form(ctx);
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
                    ("nome", nome), ("id", int.Parse(id)), ("id_usuario", usuario.Id));
                Http.Redirect(ctx, "/meus-planos");
                return;
            }

            // Plano e associação com o dono na mesma transação: um plano personalizado nunca fica sem dono
            int novoId = 0;
            Database.Transacao(conn =>
            {
                novoId = Database.InserirComId(conn,
                    "INSERT INTO plano (nome, tipo) VALUES (:nome, 'PERSONALIZADO')", "id_plano",
                    ("nome", nome));

                Database.Executar(conn,
                    "INSERT INTO usuario_plano (id_usuario, id_plano) VALUES (:id_usuario, :id_plano)",
                    ("id_usuario", usuario.Id), ("id_plano", novoId));
            });

            // Plano novo ainda não tem treinos: vai direto para a tela de treinos dele
            Http.Redirect(ctx, $"/planos/fichas?id={novoId}");
        }
        catch (Exception ex)
        {
            Http.Html(ctx, PaginaFormulario(ctx, id, nome, Erros.Traduzir(ex)));
        }
    }

    private static string PaginaFormulario(HttpListenerContext ctx, string id, string nome, string erro)
    {
        var titulo = id == "" ? "Novo plano personalizado" : "Renomear plano";

        return Template.Pagina(ctx, "meu_plano_form.html", new()
        {
            ["TITULO_FORM"] = titulo,
            ["ALERTA"] = Template.Alerta(erro),
            ["ID"] = WebUtility.HtmlEncode(id),
            ["NOME"] = WebUtility.HtmlEncode(nome),
            ["AJUDA"] = id == "" ? "Depois de salvar, você vai para a tela de treinos do plano." : ""
        }, titulo);
    }

    // POST /meus-planos/ativar
    public static void Ativar(HttpListenerContext ctx)
    {
        Alterar(ctx, (usuario, form) =>
        {
            var idPlano = int.Parse(form["id_plano"]);

            // Desativa o plano ativo e ativa o novo na mesma transação. A ordem importa: com dois
            // ativos ao mesmo tempo, o índice ux_usuplano_um_ativo barraria (regra 5).
            // Se o plano não tiver fichas, o gatilho G2 barra e o ROLLBACK mantém o plano anterior ativo.
            Database.Transacao(conn =>
            {
                Database.Executar(conn,
                    "UPDATE usuario_plano SET ativo = 0 WHERE id_usuario = :id_usuario AND ativo = 1",
                    ("id_usuario", usuario.Id));

                // MERGE: se a associação já existe (personalizado, ou predefinido já usado antes), só ativa;
                // se não existe (predefinido na primeira vez), insere já ativa.
                // O USING só encontra o plano se ele for predefinido ou personalizado do próprio usuário.
                var linhas = Database.Executar(conn, """
                    MERGE INTO usuario_plano up
                    USING (
                        SELECT :id_usuario AS id_usuario, p.id_plano
                        FROM plano p
                        WHERE p.id_plano = :id_plano
                          AND (p.tipo = 'PREDEFINIDO'
                               OR EXISTS (SELECT 1 FROM usuario_plano dono
                                          WHERE dono.id_plano = p.id_plano AND dono.id_usuario = :id_usuario))
                    ) escolhido
                    ON (up.id_usuario = escolhido.id_usuario AND up.id_plano = escolhido.id_plano)
                    WHEN MATCHED THEN
                        UPDATE SET up.ativo = 1
                    WHEN NOT MATCHED THEN
                        INSERT (id_usuario, id_plano, ativo) VALUES (escolhido.id_usuario, escolhido.id_plano, 1)
                    """,
                    ("id_usuario", usuario.Id), ("id_plano", idPlano));

                // Plano inexistente ou personalizado de outro usuário: a exceção desfaz a desativação acima
                if (linhas == 0)
                    throw new InvalidOperationException("Plano não encontrado.");
            });
            return "/meus-planos";
        });
    }

    // POST /meus-planos/desativar
    public static void Desativar(HttpListenerContext ctx)
    {
        Alterar(ctx, (usuario, form) =>
        {
            Database.Executar(
                "UPDATE usuario_plano SET ativo = 0 WHERE id_usuario = :id_usuario AND id_plano = :id_plano",
                ("id_usuario", usuario.Id), ("id_plano", int.Parse(form["id_plano"])));
            return "/meus-planos";
        });
    }

    // POST /meus-planos/excluir
    public static void ExcluirPersonalizado(HttpListenerContext ctx)
    {
        Alterar(ctx, (usuario, form) =>
        {
            // As fichas e a associação saem em cascata
            Database.Executar("""
                DELETE FROM plano
                WHERE id_plano = :id AND tipo = 'PERSONALIZADO'
                  AND EXISTS (SELECT 1 FROM usuario_plano up WHERE up.id_plano = plano.id_plano AND up.id_usuario = :id_usuario)
                """,
                ("id", int.Parse(form["id_plano"])), ("id_usuario", usuario.Id));
            return "/meus-planos";
        });
    }

    // GET /meus-planos/fichas/form e /meus-planos/fichas/form?id=   (novo treino avulso ou renomear)
    public static void FormularioFichaAvulsa(HttpListenerContext ctx)
    {
        var usuario = Autenticacao.ExigirLogin(ctx);
        if (usuario == null) return;

        var id = Http.Query(ctx, "id");
        if (id == "")
        {
            Http.Html(ctx, PaginaFormularioAvulso(ctx, "", "", ""));
            return;
        }

        var fichas = Database.Consultar(
            "SELECT nome FROM treino WHERE id_treino = :id AND tipo_treino = 'AVULSO' AND id_usuario = :id_usuario",
            ("id", int.Parse(id)), ("id_usuario", usuario.Id));

        if (fichas.Count == 0)
        {
            Http.Redirect(ctx, "/meus-planos");
            return;
        }

        Http.Html(ctx, PaginaFormularioAvulso(ctx, id, fichas[0]["NOME"]!.ToString()!, ""));
    }

    // POST /meus-planos/fichas/salvar   (cria ou renomeia um treino avulso)
    public static void SalvarFichaAvulsa(HttpListenerContext ctx)
    {
        var usuario = Autenticacao.ExigirLogin(ctx);
        if (usuario == null) return;

        var form = Http.Form(ctx);
        var id = form["id_treino"];
        var nome = form["nome"].Trim();

        try
        {
            if (id == "")
            {
                using var conn = Database.Abrir();
                var novoId = Database.InserirComId(conn,
                    "INSERT INTO treino (nome, tipo_treino, id_usuario) VALUES (:nome, 'AVULSO', :id_usuario)", "id_treino",
                    ("nome", nome), ("id_usuario", usuario.Id));

                // Treino novo ainda não tem exercícios: vai direto para a tela de exercícios dele
                Http.Redirect(ctx, $"/fichas/exercicios?id={novoId}");
            }
            else
            {
                Database.Executar(
                    "UPDATE treino SET nome = :nome WHERE id_treino = :id AND tipo_treino = 'AVULSO' AND id_usuario = :id_usuario",
                    ("nome", nome), ("id", int.Parse(id)), ("id_usuario", usuario.Id));
                Http.Redirect(ctx, "/meus-planos");
            }
        }
        catch (Exception ex)
        {
            Http.Html(ctx, PaginaFormularioAvulso(ctx, id, nome, Erros.Traduzir(ex)));
        }
    }

    private static string PaginaFormularioAvulso(HttpListenerContext ctx, string id, string nome, string erro)
    {
        var titulo = id == "" ? "Novo treino avulso" : "Renomear treino";

        return Template.Pagina(ctx, "treino_avulso_form.html", new()
        {
            ["TITULO_FORM"] = titulo,
            ["ALERTA"] = Template.Alerta(erro),
            ["ID"] = WebUtility.HtmlEncode(id),
            ["NOME"] = WebUtility.HtmlEncode(nome),
            ["AJUDA"] = id == "" ? "Depois de salvar, você vai para a tela de exercícios do treino." : ""
        }, titulo);
    }

    // POST /meus-planos/fichas/excluir
    public static void ExcluirFichaAvulsa(HttpListenerContext ctx)
    {
        Alterar(ctx, (usuario, form) =>
        {
            Database.Executar(
                "DELETE FROM treino WHERE id_treino = :id AND tipo_treino = 'AVULSO' AND id_usuario = :id_usuario",
                ("id", int.Parse(form["id_treino"])), ("id_usuario", usuario.Id));
            return "/meus-planos";
        });
    }

    // Confere o login, executa a alteração e redireciona para o endereço que ela devolver.
    // Se der erro, mostra a página com a mensagem no topo.
    private static void Alterar(HttpListenerContext ctx, Func<UsuarioLogado, Dictionary<string, string>, string> alteracao)
    {
        var usuario = Autenticacao.ExigirLogin(ctx);
        if (usuario == null) return;

        var form = Http.Form(ctx);
        try
        {
            Http.Redirect(ctx, alteracao(usuario, form));
        }
        catch (Exception ex)
        {
            Http.Html(ctx, Pagina(ctx, usuario, Erros.Traduzir(ex)));
        }
    }

    private static string Pagina(HttpListenerContext ctx, UsuarioLogado usuario, string erro)
    {
        return Template.Pagina(ctx, "meus_planos.html", new()
        {
            ["ALERTA"] = Template.Alerta(erro),
            ["CARTOES_PERSONALIZADOS"] = CartoesPlanos(usuario, "PERSONALIZADO"),
            ["CARTOES_PREDEFINIDOS"] = CartoesPlanos(usuario, "PREDEFINIDO"),
            ["LINHAS_AVULSAS"] = LinhasFichasAvulsas(usuario)
        }, "Meus planos");
    }

    // Um cartão por plano, com as fichas e os exercícios de cada uma.
    // tipo = "PREDEFINIDO": todos os predefinidos. tipo = "PERSONALIZADO": só os do usuário.
    private static string CartoesPlanos(UsuarioLogado usuario, string tipo)
    {
        // LEFT JOIN: um predefinido que o usuário nunca ativou não tem linha em usuario_plano (ativo vira 0).
        // O personalizado só aparece se tiver a linha do usuário (é dele).
        var planos = Database.Consultar("""
            SELECT p.id_plano, p.nome, NVL(up.ativo, 0) AS ativo
            FROM plano p
            LEFT JOIN usuario_plano up ON up.id_plano = p.id_plano AND up.id_usuario = :id_usuario
            WHERE p.tipo = :tipo
              AND (p.tipo = 'PREDEFINIDO' OR up.id_usuario IS NOT NULL)
            ORDER BY ativo DESC, p.nome
            """, ("id_usuario", usuario.Id), ("tipo", tipo));

        if (planos.Count == 0)
        {
            return tipo == "PREDEFINIDO"
                ? "<p class=\"text-body-secondary\">Nenhum plano predefinido cadastrado.</p>"
                : "<p class=\"text-body-secondary\">Você ainda não criou nenhum plano.</p>";
        }

        // LISTAGG junta os nomes dos exercícios de cada ficha num texto só, na ordem da ficha
        var fichas = Database.Consultar("""
            SELECT t.id_plano, t.nome,
                   LISTAGG(e.nome, ', ') WITHIN GROUP (ORDER BY te.ordem_exerc) AS exercicios
            FROM treino t
            LEFT JOIN treino_exercicio te ON te.id_treino   = t.id_treino
            LEFT JOIN exercicio e         ON e.id_exercicio = te.id_exercicio
            WHERE t.id_plano IN (
                SELECT p.id_plano
                FROM plano p
                LEFT JOIN usuario_plano up ON up.id_plano = p.id_plano AND up.id_usuario = :id_usuario
                WHERE p.tipo = :tipo
                  AND (p.tipo = 'PREDEFINIDO' OR up.id_usuario IS NOT NULL)
            )
            GROUP BY t.id_plano, t.id_treino, t.nome, t.ordem
            ORDER BY t.id_plano, t.ordem
            """, ("id_usuario", usuario.Id), ("tipo", tipo));

        var personalizado = tipo == "PERSONALIZADO";
        var html = new StringBuilder();
        foreach (var p in planos)
        {
            var id = p["ID_PLANO"]!.ToString()!;
            var ativo = Convert.ToInt32(p["ATIVO"]) == 1;
            var selos = ativo ? "<span class=\"badge text-bg-success ms-2\">Ativo</span>" : "";

            var botoes = new StringBuilder();
            botoes.Append(ativo
                ? Botao("/meus-planos/desativar", id, "Desativar", "btn-outline-secondary", "")
                : Botao("/meus-planos/ativar", id, "Ativar", "btn-outline-success", ""));

            if (personalizado)
            {
                botoes.Append($"""<a class="btn btn-sm btn-outline-primary" href="/planos/fichas?id={id}">Editar treinos</a> """);
                botoes.Append($"""<a class="btn btn-sm btn-outline-primary" href="/meus-planos/form?id={id}">Renomear</a> """);
                botoes.Append(Botao("/meus-planos/excluir", id, "Excluir", "btn-outline-danger",
                    "Excluir este plano e todos os treinos dele?"));
            }

            var itens = new StringBuilder();
            var quantidade = 0;
            foreach (var f in fichas.Where(f => f["ID_PLANO"]!.ToString() == id))
            {
                quantidade++;
                var exercicios = f["EXERCICIOS"] == null
                    ? "Sem exercícios"
                    : WebUtility.HtmlEncode(f["EXERCICIOS"]!.ToString());
                itens.Append($"""
                    <li class="list-group-item">
                      <strong>{WebUtility.HtmlEncode(f["NOME"]?.ToString())}</strong>
                      <div class="small text-body-secondary">{exercicios}</div>
                    </li>
                    """);
            }
            if (quantidade == 0)
                itens.Append("<li class=\"list-group-item text-body-secondary\">Nenhum treino neste plano.</li>");

            // Os treinos começam recolhidos; a seta ao lado do nome abre e fecha a lista.
            var borda = ativo ? " border-success" : "";
            html.Append($"""
                <div class="card mb-3{borda}">
                  <div class="card-header d-flex flex-wrap justify-content-between align-items-center gap-2">
                    <div class="d-flex align-items-center">
                      <button class="btn btn-link text-body p-0 me-2 seta-expandir" type="button"
                              data-bs-toggle="collapse" data-bs-target="#treinos-plano-{id}" aria-expanded="false" title="Mostrar treinos">
                        <svg width="16" height="16" viewBox="0 0 16 16" aria-hidden="true">
                          <path d="M6 3.5 10.5 8 6 12.5" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" />
                        </svg>
                      </button>
                      <strong>{WebUtility.HtmlEncode(p["NOME"]?.ToString())}</strong>{selos}
                    </div>
                    <div class="text-nowrap">{botoes}</div>
                  </div>
                  <div class="collapse" id="treinos-plano-{id}">
                    <ul class="list-group list-group-flush">{itens}</ul>
                  </div>
                </div>
                """);
        }
        return html.ToString();
    }

    // Botão de ação dentro de um formulário POST, com confirmação opcional
    private static string Botao(string acao, string idPlano, string texto, string classe, string confirmacao)
    {
        var onsubmit = confirmacao == "" ? "" : $" onsubmit=\"return confirm('{confirmacao}')\"";
        return $"""
            <form method="post" action="{acao}" class="d-inline"{onsubmit}>
              <input type="hidden" name="id_plano" value="{idPlano}">
              <button class="btn btn-sm {classe}">{texto}</button>
            </form>
            """;
    }

    private static string LinhasFichasAvulsas(UsuarioLogado usuario)
    {
        var fichas = Database.Consultar("""
            SELECT t.id_treino, t.nome,
                   (SELECT COUNT(*) FROM treino_exercicio te WHERE te.id_treino = t.id_treino) AS exercicios
            FROM treino t
            WHERE t.tipo_treino = 'AVULSO' AND t.id_usuario = :id_usuario
            ORDER BY t.nome
            """, ("id_usuario", usuario.Id));

        var linhas = new StringBuilder();
        foreach (var f in fichas)
        {
            var id = f["ID_TREINO"];
            linhas.Append($"""
                <tr>
                  <td>{WebUtility.HtmlEncode(f["NOME"]?.ToString())}</td>
                  <td>{f["EXERCICIOS"]}</td>
                  <td class="text-end text-nowrap">
                    <a class="btn btn-sm btn-outline-secondary" href="/fichas/exercicios?id={id}">Exercícios</a>
                    <a class="btn btn-sm btn-outline-primary" href="/meus-planos/fichas/form?id={id}">Renomear</a>
                    <form method="post" action="/meus-planos/fichas/excluir" class="d-inline" onsubmit="return confirm('Excluir este treino avulso?')">
                      <input type="hidden" name="id_treino" value="{id}">
                      <button class="btn btn-sm btn-outline-danger">Excluir</button>
                    </form>
                  </td>
                </tr>
                """);
        }

        if (fichas.Count == 0)
            linhas.Append("<tr><td colspan=\"3\" class=\"text-body-secondary\">Nenhum treino avulso.</td></tr>");

        return linhas.ToString();
    }
}

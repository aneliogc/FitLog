using FitLog.Infra;
using System.Globalization;
using System.Net;
using System.Text;

namespace FitLog.Handlers;

// Página inicial: saudação, botão de registrar treino e resumo do último treino realizado
public static class InicioHandler
{
    private static readonly CultureInfo Brasil = new("pt-BR");

    // GET /
    public static void Exibir(HttpListenerContext ctx)
    {
        var usuario = Autenticacao.ExigirLogin(ctx);
        if (usuario == null) return;

        Http.Html(ctx, Template.Pagina(ctx, "inicio.html", new()
        {
            ["NOME"] = WebUtility.HtmlEncode(usuario.Nome),
            ["ULTIMO_TREINO"] = UltimoTreino(usuario)
        }, "Início"));
    }

    private static string UltimoTreino(UsuarioLogado usuario)
    {
        // O mais recente pelo horário de início; FETCH FIRST traz só uma linha
        var sessoes = Database.Consultar("""
            SELECT s.id_sessao, s.data, s.hora_inicio, s.hora_fim, t.nome AS treino
            FROM sessao s
            LEFT JOIN treino t ON t.id_treino = s.id_treino
            WHERE s.id_usuario = :id_usuario
            ORDER BY s.hora_inicio DESC
            FETCH FIRST 1 ROW ONLY
            """, ("id_usuario", usuario.Id));

        if (sessoes.Count == 0)
            return "<p class=\"text-body-secondary mb-0\">Você ainda não registrou nenhum treino.</p>";

        var s = sessoes[0];
        var id = s["ID_SESSAO"];
        var inicio = (DateTime)s["HORA_INICIO"]!;
        var fim = (DateTime)s["HORA_FIM"]!;
        var treino = s["TREINO"] == null ? "Treino livre" : s["TREINO"]!.ToString()!;

        // Um resumo por exercício: número de séries realizadas
        var exercicios = Database.Consultar("""
            SELECT e.nome, e.tipo, COUNT(*) AS series
            FROM serie se
            JOIN exercicio e ON e.id_exercicio = se.id_exercicio
            WHERE se.id_sessao = :id_sessao
            GROUP BY e.id_exercicio, e.nome, e.tipo
            ORDER BY e.nome
            """, ("id_sessao", id));

        var itens = new StringBuilder();
        foreach (var e in exercicios)
        {
            var series = Convert.ToInt32(e["SERIES"]);
            var textoSeries = series == 1 ? "1 série" : $"{series} séries";
            itens.Append($"""
                <li class="list-group-item d-flex justify-content-between">
                  <span>{WebUtility.HtmlEncode(e["NOME"]?.ToString())}</span>
                  <span class="text-body-secondary">{textoSeries}</span>
                </li>
                """);
        }

        if (exercicios.Count == 0)
            itens.Append("<li class=\"list-group-item text-body-secondary\">Nenhuma série registrada.</li>");

        return $"""
            <div class="card">
              <div class="card-header d-flex flex-wrap justify-content-between align-items-center gap-2">
                <div class="d-flex align-items-center">
                  <button class="btn btn-link text-body p-0 me-2 seta-expandir" type="button"
                          data-bs-toggle="collapse" data-bs-target="#exercicios-ultimo-treino" aria-expanded="false" title="Mostrar exercícios">
                    <svg width="16" height="16" viewBox="0 0 16 16" aria-hidden="true">
                      <path d="M6 3.5 10.5 8 6 12.5" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" />
                    </svg>
                  </button>
                  <strong>{WebUtility.HtmlEncode(treino)}</strong>
                  <span class="text-body-secondary ms-2">{inicio:dd/MM/yyyy} · {inicio:HH:mm} – {fim:HH:mm} ({(int)(fim - inicio).TotalMinutes} min)</span>
                </div>
                <a class="btn btn-sm btn-outline-secondary" href="/treinos/series?id={id}">Ver séries</a>
              </div>
              <div class="collapse" id="exercicios-ultimo-treino">
                <ul class="list-group list-group-flush">{itens}</ul>
              </div>
            </div>
            """;
    }
}

using FitLog.Infra;
using System.Net;
using System.Text;

namespace FitLog.Handlers;

// Usuários (área do administrador)
public static class UsuarioHandler
{
    // GET /usuarios
    public static void Listar(HttpListenerContext ctx)
    {
        var admin = Autenticacao.ExigirAdmin(ctx);
        if (admin == null) return;

        Http.Html(ctx, PaginaLista(ctx, admin, ""));
    }

    // POST /usuarios/perfil
    public static void AlterarPerfil(HttpListenerContext ctx)
    {
        var admin = Autenticacao.ExigirAdmin(ctx);
        if (admin == null) return;

        var form = Http.Form(ctx);
        var id = int.Parse(form["id"]);
        var perfil = form["perfil"];

        // O admin não muda o próprio perfil, para não perder o acesso a esta tela
        if (id == admin.Id)
        {
            Http.Html(ctx, PaginaLista(ctx, admin, "Você não pode alterar o seu próprio perfil."));
            return;
        }

        try
        {
            Database.Executar(
                "UPDATE usuario SET perfil = :perfil WHERE id_usuario = :id",
                ("perfil", perfil), ("id", id));
            Autenticacao.AtualizarPerfil(id, perfil);
            Http.Redirect(ctx, "/usuarios");
        }
        catch (Exception ex)
        {
            Http.Html(ctx, PaginaLista(ctx, admin, Erros.Traduzir(ex)));
        }
    }

    // POST /usuarios/excluir
    public static void Excluir(HttpListenerContext ctx)
    {
        var admin = Autenticacao.ExigirAdmin(ctx);
        if (admin == null) return;

        var id = int.Parse(Http.Form(ctx)["id"]);

        if (id == admin.Id)
        {
            Http.Html(ctx, PaginaLista(ctx, admin, "Para excluir a sua própria conta, use Minha conta."));
            return;
        }

        try
        {
            ExcluirConta(id);
            Http.Redirect(ctx, "/usuarios");
        }
        catch (Exception ex)
        {
            Http.Html(ctx, PaginaLista(ctx, admin, Erros.Traduzir(ex)));
        }
    }

    // Também usado por Minha conta.
    // O banco apaga em cascata as associações com planos, as fichas avulsas e os treinos realizados,
    // mas os planos personalizados não têm FK para o usuário e ficariam sem dono: saem antes, na mesma transação.
    public static void ExcluirConta(int idUsuario)
    {
        Database.Transacao(conn =>
        {
            Database.Executar(conn, """
                DELETE FROM plano
                WHERE tipo = 'PERSONALIZADO'
                  AND id_plano IN (SELECT id_plano FROM usuario_plano WHERE id_usuario = :id)
                """, ("id", idUsuario));

            Database.Executar(conn, "DELETE FROM usuario WHERE id_usuario = :id", ("id", idUsuario));
        });

        Autenticacao.EncerrarSessoes(idUsuario);
    }

    private static string PaginaLista(HttpListenerContext ctx, UsuarioLogado admin, string erro)
    {
        var usuarios = Database.Consultar("""
            SELECT id_usuario, nome, login, email, data_nasc, perfil
            FROM usuario
            ORDER BY nome
            """);

        var linhas = new StringBuilder();
        foreach (var u in usuarios)
        {
            var id = Convert.ToInt32(u["ID_USUARIO"]);
            var perfil = u["PERFIL"]!.ToString();
            var ehAdmin = perfil == "ADMIN";
            var dataNasc = u["DATA_NASC"] is DateTime d ? d.ToString("dd/MM/yyyy") : "";

            linhas.Append("<tr>");
            linhas.Append($"<td>{WebUtility.HtmlEncode(u["NOME"]?.ToString())}</td>");
            linhas.Append($"<td>{WebUtility.HtmlEncode(u["LOGIN"]?.ToString())}</td>");
            linhas.Append($"<td>{WebUtility.HtmlEncode(u["EMAIL"]?.ToString())}</td>");
            linhas.Append($"<td>{dataNasc}</td>");
            linhas.Append(ehAdmin
                ? "<td><span class=\"badge text-bg-primary\">Admin</span></td>"
                : "<td><span class=\"badge text-bg-secondary\">Comum</span></td>");

            if (id == admin.Id)
            {
                linhas.Append("<td></td>");
            }
            else
            {
                var novoPerfil = ehAdmin ? "COMUM" : "ADMIN";
                var textoBotao = ehAdmin ? "Remover permissão" : "Elevar permissão";
                linhas.Append($"""
                    <td class="text-end text-nowrap">
                      <form method="post" action="/usuarios/perfil" class="d-inline">
                        <input type="hidden" name="id" value="{id}">
                        <input type="hidden" name="perfil" value="{novoPerfil}">
                        <button class="btn btn-sm btn-outline-primary">{textoBotao}</button>
                      </form>
                      <form method="post" action="/usuarios/excluir" class="d-inline" onsubmit="return confirm('Excluir este usuário? Os planos personalizados, as fichas avulsas e os treinos dele também serão apagados.')">
                        <input type="hidden" name="id" value="{id}">
                        <button class="btn btn-sm btn-outline-danger">Excluir</button>
                      </form>
                    </td>
                    """);
            }
            linhas.Append("</tr>");
        }

        return Template.Pagina(ctx, "usuarios.html", new()
        {
            ["ALERTA"] = Template.Alerta(erro),
            ["LINHAS"] = linhas.ToString()
        }, "Usuários");
    }
}

using System.Net;

namespace FitLog.Infra;

public static class Template
{
    private static readonly string Pasta = Path.Combine(AppContext.BaseDirectory, "Templates");

    public static string Pagina(HttpListenerContext ctx, string arquivo, Dictionary<string, string> valores, string titulo)
    {
        var conteudo = File.ReadAllText(Path.Combine(Pasta, arquivo));
        foreach (var (chave, valor) in valores)
            conteudo = conteudo.Replace("{{" + chave + "}}", valor);

        var layout = File.ReadAllText(Path.Combine(Pasta, "layout.html"));
        return layout
            .Replace("{{TITULO}}", WebUtility.HtmlEncode(titulo))
            .Replace("{{MENU}}", Menu(Autenticacao.Atual(ctx), ctx.Request.Url!.AbsolutePath))
            .Replace("{{CONTEUDO}}", conteudo);
    }

    public static string Alerta(string mensagem)
    {
        if (mensagem == "") return "";
        return $"<div class=\"alert alert-danger\">{WebUtility.HtmlEncode(mensagem)}</div>";
    }

    private static string Menu(UsuarioLogado? usuario, string caminho)
    {
        if (usuario == null)
        {
            return "";
        }

        var admin = "";
        if (usuario.Perfil == "ADMIN")
        {
            string[] rotasAdmin = ["/grupos", "/exercicios", "/planos", "/usuarios"];
            var adminAtivo = rotasAdmin.Any(r => EstaEm(caminho, r)) ? " active" : "";

            admin = $"""
                <li class="nav-item dropdown">
                  <a class="nav-link dropdown-toggle{adminAtivo}" href="#" data-bs-toggle="dropdown">Administração</a>
                  <ul class="dropdown-menu">
                    {ItemDropdown("/grupos", "Grupos musculares", caminho)}
                    {ItemDropdown("/exercicios", "Exercícios", caminho)}
                    {ItemDropdown("/planos", "Planos predefinidos", caminho)}
                    {ItemDropdown("/usuarios", "Usuários", caminho)}
                  </ul>
                </li>
                """;
        }

        // O botão "sanduíche" e o menu ficam aqui, e não no layout, para não aparecerem sem login
        return $"""
            <button class="navbar-toggler" type="button" data-bs-toggle="collapse" data-bs-target="#menu">
              <span class="navbar-toggler-icon"></span>
            </button>
            <div class="collapse navbar-collapse" id="menu">
              <ul class="navbar-nav me-auto ms-lg-4">
                {Item("/meus-planos", "Meus planos", caminho)}
                {Item("/meus-treinos", "Meus treinos", caminho)}
                {Item("/sessoes", "Sessões", caminho)}
                {Item("/relatorios", "Relatórios", caminho)}
                {admin}
              </ul>
              <ul class="navbar-nav">
                <li class="nav-item dropdown">
                  <a class="nav-link dropdown-toggle" href="#" data-bs-toggle="dropdown">{WebUtility.HtmlEncode(usuario.Nome)}</a>
                  <ul class="dropdown-menu dropdown-menu-end">
                    {ItemDropdown("/minha-conta", "Minha conta", caminho)}
                    <li><hr class="dropdown-divider"></li>
                    <li>
                      <form method="post" action="/logout">
                        <button class="dropdown-item text-danger">Sair</button>
                      </form>
                    </li>
                  </ul>
                </li>
              </ul>
            </div>
            """;
    }

    // "/grupos/form" também conta como estar em "/grupos"
    private static bool EstaEm(string caminho, string rota)
    {
        return caminho == rota || caminho.StartsWith(rota + "/");
    }

    private static string Item(string rota, string texto, string caminho)
    {
        var ativo = EstaEm(caminho, rota) ? " active" : "";
        return $"""<li class="nav-item"><a class="nav-link{ativo}" href="{rota}">{texto}</a></li>""";
    }

    private static string ItemDropdown(string rota, string texto, string caminho)
    {
        var ativo = EstaEm(caminho, rota) ? " active" : "";
        return $"""<li><a class="dropdown-item{ativo}" href="{rota}">{texto}</a></li>""";
    }
}

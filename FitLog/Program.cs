using FitLog.Handlers;
using FitLog.Infra;
using System.Net;

var listener = new HttpListener();
listener.Prefixes.Add("http://localhost:8080/");
listener.Start();
Console.WriteLine("Servidor rodando em http://localhost:8080/");

while (true)
{
    var ctx = listener.GetContext();
    var metodo = ctx.Request.HttpMethod;
    var caminho = ctx.Request.Url!.AbsolutePath;
    Console.WriteLine(metodo + " " + caminho);

    try
    {
        if (metodo == "GET")
        {
            switch (caminho)
            {
                case "/":
                    var usuario = Autenticacao.ExigirLogin(ctx);
                    if (usuario == null) break;
                    Http.Html(ctx, Template.Pagina(ctx, "inicio.html", new()
                    {
                        ["NOME"] = WebUtility.HtmlEncode(usuario.Nome)
                    }, "Início"));
                    break;

                // Conta (públicas)
                case "/login":        ContaHandler.FormularioLogin(ctx); break;
                case "/cadastro":     ContaHandler.FormularioCadastro(ctx); break;

                // Grupos musculares
                case "/grupos":       GrupoHandler.Listar(ctx); break;
                case "/grupos/form":  GrupoHandler.Formulario(ctx); break;

                // Exercícios
                case "/exercicios":       ExercicioHandler.Listar(ctx); break;
                case "/exercicios/form":  ExercicioHandler.Formulario(ctx); break;
                case "/exercicios/grupos": ExercicioHandler.Grupos(ctx); break;

                // Planos predefinidos e fichas
                case "/planos":              PlanoHandler.Listar(ctx); break;
                case "/planos/form":         PlanoHandler.Formulario(ctx); break;
                case "/planos/fichas":       TreinoHandler.FichasDoPlano(ctx); break;
                case "/fichas/exercicios":   TreinoExercicioHandler.Listar(ctx); break;

                // Usuários
                case "/usuarios":            UsuarioHandler.Listar(ctx); break;

                // Minha conta
                case "/minha-conta":         MinhaContaHandler.Exibir(ctx); break;
                case "/minha-conta/senha":   MinhaContaHandler.FormularioSenha(ctx); break;

                // Meus planos
                case "/meus-planos":         MeusPlanosHandler.Exibir(ctx); break;

                default: NaoEncontrada(ctx); break;
            }
        }
        else if (metodo == "POST")
        {
            switch (caminho)
            {
                // Conta (públicas)
                case "/login":          ContaHandler.Entrar(ctx); break;
                case "/logout":         ContaHandler.Sair(ctx); break;
                case "/cadastro":       ContaHandler.Cadastrar(ctx); break;

                // Grupos musculares
                case "/grupos/salvar":   GrupoHandler.Salvar(ctx); break;
                case "/grupos/excluir":  GrupoHandler.Excluir(ctx); break;

                // Exercícios
                case "/exercicios/salvar":   ExercicioHandler.Salvar(ctx); break;
                case "/exercicios/excluir":  ExercicioHandler.Excluir(ctx); break;
                case "/exercicios/grupos/incluir":    ExercicioHandler.IncluirGrupo(ctx); break;
                case "/exercicios/grupos/principal":  ExercicioHandler.TornarPrincipal(ctx); break;
                case "/exercicios/grupos/remover":    ExercicioHandler.RemoverGrupo(ctx); break;

                // Planos predefinidos e fichas
                case "/planos/salvar":               PlanoHandler.Salvar(ctx); break;
                case "/planos/excluir":              PlanoHandler.Excluir(ctx); break;
                case "/planos/fichas/salvar":        TreinoHandler.SalvarFicha(ctx); break;
                case "/planos/fichas/excluir":       TreinoHandler.ExcluirFicha(ctx); break;
                case "/planos/fichas/mover":         TreinoHandler.MoverFicha(ctx); break;
                case "/fichas/exercicios/incluir":   TreinoExercicioHandler.Incluir(ctx); break;
                case "/fichas/exercicios/remover":   TreinoExercicioHandler.Remover(ctx); break;
                case "/fichas/exercicios/mover":     TreinoExercicioHandler.Mover(ctx); break;

                // Usuários
                case "/usuarios/perfil":             UsuarioHandler.AlterarPerfil(ctx); break;
                case "/usuarios/excluir":            UsuarioHandler.Excluir(ctx); break;

                // Minha conta
                case "/minha-conta/salvar":          MinhaContaHandler.SalvarDados(ctx); break;
                case "/minha-conta/senha":           MinhaContaHandler.AlterarSenha(ctx); break;
                case "/minha-conta/excluir":         MinhaContaHandler.Excluir(ctx); break;

                // Meus planos
                case "/meus-planos/salvar":          MeusPlanosHandler.SalvarPersonalizado(ctx); break;
                case "/meus-planos/ativar":          MeusPlanosHandler.Ativar(ctx); break;
                case "/meus-planos/desativar":       MeusPlanosHandler.Desativar(ctx); break;
                case "/meus-planos/excluir":         MeusPlanosHandler.ExcluirPersonalizado(ctx); break;
                case "/meus-planos/fichas/salvar":   MeusPlanosHandler.SalvarFichaAvulsa(ctx); break;
                case "/meus-planos/fichas/excluir":  MeusPlanosHandler.ExcluirFichaAvulsa(ctx); break;

                default: NaoEncontrada(ctx); break;
            }
        }
        else
        {
            NaoEncontrada(ctx);
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine("ERRO: " + ex);

        try
        {
            Http.Html(ctx, $"<h1>Ops!</h1><p>{WebUtility.HtmlEncode(Erros.Traduzir(ex))}</p>", 500);
        }
        catch { }
    }
}

static void NaoEncontrada(HttpListenerContext ctx)
{
    Http.Html(ctx, "<h1>Página não encontrada</h1>", 404);
}

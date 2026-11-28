using Oracle.ManagedDataAccess.Client;

namespace FitLog.Infra;

public static class Erros
{
    public static string Traduzir(Exception ex)
    {
        // int.Parse / decimal.Parse / DateTime.ParseExact com texto inválido
        if (ex is FormatException)
            return "Algum número, data ou hora foi digitado num formato inválido.";

        if (ex is not OracleException oex)
            return "Erro inesperado: " + ex.Message;

        if (oex.Number >= 20000 && oex.Number <= 20999)
        {
            var primeiraLinha = oex.Message.Split('\n')[0];
            return primeiraLinha[(primeiraLinha.IndexOf(':') + 1)..].Trim();
        }

        if (oex.Number == 1)
            return Duplicado(oex.Message);

        if (oex.Number == 2290)
            return Verificacao(oex.Message);

        return oex.Number switch
        {
            1400 => "Preencha todos os campos obrigatórios.",
            2291 => "O registro relacionado não existe.",
            2292 => "Este registro está em uso e não pode ser excluído.",
            _ => "Erro no banco: " + oex.Message
        };
    }

    private static string Duplicado(string mensagem)
    {
        var msg = mensagem.ToUpperInvariant();

        if (msg.Contains("UK_USUARIO_LOGIN")) return "Este login já está em uso.";
        if (msg.Contains("UK_USUARIO_EMAIL")) return "Este e-mail já está cadastrado.";
        if (msg.Contains("UK_GRUPO_NOME")) return "Já existe um grupo muscular com este nome.";
        if (msg.Contains("UK_EXERCICIO_NOME")) return "Já existe um exercício com este nome.";
        if (msg.Contains("PK_EXERCICIO_GRUPO")) return "Este grupo muscular já está associado ao exercício.";
        if (msg.Contains("UX_EXGRUPO_UM_PRINCIPAL")) return "Este exercício já tem um grupo muscular principal.";
        if (msg.Contains("PK_USUARIO_PLANO")) return "Este plano já está associado a você.";
        if (msg.Contains("UX_USUPLANO_UM_ATIVO")) return "Você já tem um plano ativo.";
        if (msg.Contains("UK_TREINO_PLANO_ORDEM")) return "Já existe um treino nesta posição do plano.";
        if (msg.Contains("PK_TREINO_EXERCICIO")) return "Este exercício já está no treino.";
        if (msg.Contains("UK_TREINOEX_ORDEM")) return "Já existe um exercício nesta posição do treino.";

        return "Já existe um registro com esses dados.";
    }

    // CHECK violado
    private static string Verificacao(string mensagem)
    {
        var msg = mensagem.ToUpperInvariant();

        if (msg.Contains("CK_SESSAO_HORARIO")) return "O fim do treino precisa ser depois do início.";
        if (msg.Contains("CK_SESSAO_DATA")) return "A data do treino precisa ser o dia do horário de início.";
        if (msg.Contains("CK_SERIE_CARGA")) return "A carga não pode ser negativa.";
        if (msg.Contains("CK_SERIE_REPETICOES")) return "O número de repetições precisa ser maior que zero.";
        if (msg.Contains("CK_SERIE_DESCANSO")) return "O descanso não pode ser negativo.";
        if (msg.Contains("CK_TREINO_ESPECIALIZACAO")) return "Um treino é de um plano (com posição maior que zero) ou avulso, nunca os dois.";
        if (msg.Contains("CK_TREINOEX_ORDEM")) return "A posição do exercício no treino precisa ser maior que zero.";

        return "Algum valor informado não é permitido.";
    }
}
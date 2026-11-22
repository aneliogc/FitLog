using Oracle.ManagedDataAccess.Client;

namespace FitLog.Infra;

public static class Erros
{
    public static string Traduzir(Exception ex)
    {
        if (ex is not OracleException oex)
            return "Erro inesperado: " + ex.Message;

        if (oex.Number >= 20000 && oex.Number <= 20999)
        {
            var primeiraLinha = oex.Message.Split('\n')[0];
            return primeiraLinha[(primeiraLinha.IndexOf(':') + 1)..].Trim();
        }

        return oex.Number switch
        {
            1 => "Já existe um registro com esses dados.",
            1400 => "Preencha todos os campos obrigatórios.",
            2290 => "Algum valor informado não é permitido.",
            2291 => "O registro relacionado não existe.",
            2292 => "Este registro está em uso e não pode ser excluído.",
            _ => "Erro no banco: " + oex.Message
        };
    }
}
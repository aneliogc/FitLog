using Oracle.ManagedDataAccess.Client;

namespace FitLog.Infra;

public static class Database
{
    private static readonly string ConnStr;

    static Database()
    {
        OracleConfiguration.TnsAdmin = @"C:\Wallet_ECBDT";
        OracleConfiguration.WalletLocation = @"C:\Wallet_ECBDT";

        // A senha fica fora do código para não ir para o GitHub
        var senha = Environment.GetEnvironmentVariable("FITLOG_DB_SENHA")
            ?? throw new InvalidOperationException("Defina a variável de ambiente FITLOG_DB_SENHA com a senha do banco.");

        ConnStr = $"User Id=ECLBDIT101;Password={senha};Data Source=ecbdt_high";
    }

    public static OracleConnection Abrir()
    {
        var conn = new OracleConnection(ConnStr);
        conn.Open();
        return conn;
    }

    public static List<Dictionary<string, object?>> Consultar(string sql, params (string Nome, object? Valor)[] parametros)
    {
        using var conn = Abrir();
        using var cmd = new OracleCommand(sql, conn) { BindByName = true };
        foreach (var (nome, valor) in parametros)
            cmd.Parameters.Add(nome, valor ?? DBNull.Value);
        using var leitor = cmd.ExecuteReader();

        var linhas = new List<Dictionary<string, object?>>();
        while (leitor.Read())
        {
            var linha = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < leitor.FieldCount; i++)
                linha[leitor.GetName(i)] = leitor.IsDBNull(i) ? null : leitor.GetValue(i);
            linhas.Add(linha);
        }
        return linhas;
    }

    public static int Executar(string sql, params (string Nome, object? Valor)[] parametros)
    {
        using var conn = Abrir();
        using var cmd = new OracleCommand(sql, conn) { BindByName = true };
        foreach (var (nome, valor) in parametros)
            cmd.Parameters.Add(nome, valor ?? DBNull.Value);
        return cmd.ExecuteNonQuery();
    }
}
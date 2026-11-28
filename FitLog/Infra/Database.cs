using Oracle.ManagedDataAccess.Client;

namespace FitLog.Infra;

public static class Database
{
    private static readonly string ConnStr;

    static Database()
    {
        OracleConfiguration.TnsAdmin = @"C:\Wallet_ECBDT";
        OracleConfiguration.WalletLocation = @"C:\Wallet_ECBDT";

        // A senha fica fora do código
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
        return Executar(conn, sql, parametros);
    }

    // Executa usando uma conexão já aberta (usada dentro de Transacao)
    public static int Executar(OracleConnection conn, string sql, params (string Nome, object? Valor)[] parametros)
    {
        using var cmd = new OracleCommand(sql, conn) { BindByName = true };
        foreach (var (nome, valor) in parametros)
            cmd.Parameters.Add(nome, valor ?? DBNull.Value);
        return cmd.ExecuteNonQuery();
    }

    // INSERT que devolve o id gerado pelo IDENTITY, com "RETURNING coluna INTO :novo_id".
    public static int InserirComId(OracleConnection conn, string sql, string colunaId, params (string Nome, object? Valor)[] parametros)
    {
        using var cmd = new OracleCommand(sql + " RETURNING " + colunaId + " INTO :novo_id", conn) { BindByName = true };
        foreach (var (nome, valor) in parametros)
            cmd.Parameters.Add(nome, valor ?? DBNull.Value);

        var novoId = new OracleParameter("novo_id", OracleDbType.Int32, System.Data.ParameterDirection.Output);
        cmd.Parameters.Add(novoId);

        cmd.ExecuteNonQuery();
        return ((Oracle.ManagedDataAccess.Types.OracleDecimal)novoId.Value).ToInt32();
    }

    // Executa vários comandos como uma unidade: ou todos são gravados (COMMIT), ou nenhum (ROLLBACK).
    public static void Transacao(Action<OracleConnection> comandos)
    {
        using var conn = Abrir();

        Executar(conn, "ALTER SESSION DISABLE PARALLEL DML");

        using var transacao = conn.BeginTransaction();
        try
        {
            comandos(conn);
            transacao.Commit();
        }
        catch
        {
            transacao.Rollback();
            throw;
        }
    }
}
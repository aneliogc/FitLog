using System.Security.Cryptography;

namespace FitLog.Infra;

public static class Senha
{
    private const int Iteracoes = 100_000;

    public static string GerarHash(string senha)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(senha, salt, Iteracoes, HashAlgorithmName.SHA256, 32);
        return Convert.ToBase64String(salt) + ":" + Convert.ToBase64String(hash);
    }

    public static bool Verificar(string senha, string guardado)
    {
        var partes = guardado.Split(':');
        if (partes.Length != 2) return false;

        try
        {
            var salt = Convert.FromBase64String(partes[0]);
            var hashGuardado = Convert.FromBase64String(partes[1]);
            var hashDigitado = Rfc2898DeriveBytes.Pbkdf2(senha, salt, Iteracoes, HashAlgorithmName.SHA256, 32);

            return CryptographicOperations.FixedTimeEquals(hashDigitado, hashGuardado);
        }
        catch (FormatException)
        {
            // Senha gravada fora do formato "salt:hash" (ex.: inserida à mão no banco)
            return false;
        }
    }
}
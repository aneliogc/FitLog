using System.Net;
using System.Text;

namespace FitLog.Infra;

public static class Http
{
    public static void Html(HttpListenerContext ctx, string html, int status = 200)
    {
        var bytes = Encoding.UTF8.GetBytes(html);
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "text/html; charset=utf-8";
        ctx.Response.ContentLength64 = bytes.Length;
        ctx.Response.OutputStream.Write(bytes);
        ctx.Response.Close();
    }

    public static Dictionary<string, string> Form(HttpListenerContext ctx)
    {
        using var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8);
        var dados = System.Web.HttpUtility.ParseQueryString(reader.ReadToEnd());

        var form = new Dictionary<string, string>();
        foreach (string? chave in dados.AllKeys)
            if (chave != null) form[chave] = dados[chave] ?? "";
        return form;
    }

    public static void Redirect(HttpListenerContext ctx, string url)
    {
        ctx.Response.Redirect(url);
        ctx.Response.Close();
    }
}
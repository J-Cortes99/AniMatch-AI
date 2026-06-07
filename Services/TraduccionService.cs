using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using AnimeRecommender.Options;

namespace AnimeRecommender.Services;

// Traduce un texto al español con el modelo local. Cachea el resultado en memoria.
// Lanza OperationCanceledException si se cancela o se agota el timeout (el endpoint lo mapea).
public sealed class TraduccionService(IChatClient chat, IMemoryCache cache, IOptions<OllamaOptions> opciones)
{
    private readonly TimeSpan _timeout = opciones.Value.Timeout;

    public async Task<string> TraducirAsync(string texto, CancellationToken ct)
    {
        texto = (texto ?? "").Trim();
        if (texto.Length == 0) return "";

        var clave = "trad:" + texto.GetHashCode();
        if (cache.TryGetValue(clave, out string? cacheado))
            return cacheado!;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_timeout);

        var prompt = $$"""
            Traduce al español, de forma natural y fluida, el siguiente texto.
            Devuelve ÚNICAMENTE la traducción, sin comillas ni notas ni texto adicional.

            {{texto}}
            """;

        var r = await chat.GetResponseAsync(prompt, cancellationToken: cts.Token);
        var traduccion = (r.Text ?? "").Trim();
        cache.Set(clave, traduccion, TimeSpan.FromDays(1));
        return traduccion;
    }
}

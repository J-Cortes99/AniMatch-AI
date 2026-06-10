using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using AnimeRecommender.Options;

namespace AnimeRecommender.Services;

// Traduce al español la sinopsis de un anime, identificado por su id de MAL. El texto
// lo obtiene el propio servidor (vía JikanService), nunca el cliente: así el endpoint
// no puede usarse como traductor de texto arbitrario. Cachea por id en memoria.
// Devuelve null si el anime no existe o no tiene sinopsis.
// Lanza OperationCanceledException si se cancela o se agota el timeout (el endpoint lo mapea).
public sealed class TraduccionService(
    IChatClient chat, JikanService jikan, IMemoryCache cache, IOptions<ModeloOptions> opciones)
{
    private readonly TimeSpan _timeout = opciones.Value.Timeout;

    public async Task<string?> TraducirSinopsisAsync(int malId, CancellationToken ct)
    {
        var clave = "trad:" + malId;
        if (cache.TryGetValue(clave, out string? cacheado))
            return cacheado;

        var sinopsis = await jikan.ObtenerSinopsisAsync(malId, ct);
        if (string.IsNullOrWhiteSpace(sinopsis))
            return null;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_timeout);

        var prompt = $$"""
            Traduce al español, de forma natural y fluida, el siguiente texto.
            Devuelve ÚNICAMENTE la traducción, sin comillas ni notas ni texto adicional.

            {{sinopsis}}
            """;

        var r = await chat.GetResponseAsync(prompt, cancellationToken: cts.Token);
        var traduccion = (r.Text ?? "").Trim();
        if (traduccion.Length == 0) return null;   // respuesta vacía: no la cacheamos

        cache.Set(clave, traduccion, TimeSpan.FromDays(7));
        return traduccion;
    }
}

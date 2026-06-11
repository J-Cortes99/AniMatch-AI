using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using AnimeRecommender.Options;

namespace AnimeRecommender.Services;

// Traduce al español la sinopsis de un anime, identificado por su id de MAL. El texto
// lo obtiene el propio servidor (vía JikanService), nunca el cliente: así el endpoint
// no puede usarse como traductor de texto arbitrario.
// Tres niveles: caché de memoria → Postgres (sobrevive a los redeploys: cada sinopsis
// se paga al modelo UNA vez) → modelo. Devuelve null si el anime no existe o no tiene
// sinopsis. Lanza OperationCanceledException si se cancela o agota el timeout.
public sealed class TraduccionService(
    IChatClient chat, JikanService jikan, IMemoryCache cache, BaseDatos bd,
    IOptions<ModeloOptions> opciones, ILogger<TraduccionService> log)
{
    private readonly TimeSpan _timeout = opciones.Value.Timeout;

    public async Task<string?> TraducirSinopsisAsync(int malId, CancellationToken ct)
    {
        var clave = "trad:" + malId;
        if (cache.TryGetValue(clave, out string? cacheado))
            return cacheado;

        if (bd.Disponible && await ObtenerGuardadaAsync(malId, ct) is { } guardada)
        {
            cache.Set(clave, guardada, TimeSpan.FromDays(7));
            return guardada;
        }

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
        if (bd.Disponible)
        {
            // Mejor perder la persistencia puntual que fallarle al usuario con la
            // traducción ya en la mano.
            try { await GuardarAsync(malId, traduccion, ct); }
            catch (Exception ex) { log.LogWarning(ex, "No se pudo persistir la traducción de {MalId}", malId); }
        }
        return traduccion;
    }

    private async Task<string?> ObtenerGuardadaAsync(int malId, CancellationToken ct)
    {
        await using var cmd = bd.Fuente.CreateCommand("SELECT texto FROM traducciones WHERE mal_id = $1");
        cmd.Parameters.AddWithValue(malId);
        return await cmd.ExecuteScalarAsync(ct) as string;
    }

    private async Task GuardarAsync(int malId, string texto, CancellationToken ct)
    {
        await using var cmd = bd.Fuente.CreateCommand("""
            INSERT INTO traducciones(mal_id, texto) VALUES ($1, $2)
            ON CONFLICT (mal_id) DO NOTHING
            """);
        cmd.Parameters.AddWithValue(malId);
        cmd.Parameters.AddWithValue(texto);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}

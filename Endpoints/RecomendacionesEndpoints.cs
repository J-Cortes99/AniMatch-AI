using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;
using AnimeRecommender.Models;
using AnimeRecommender.Options;
using AnimeRecommender.Services;

namespace AnimeRecommender.Endpoints;

public static class RecomendacionesEndpoints
{
    private static readonly JsonSerializerOptions JsonWeb = new(JsonSerializerDefaults.Web);   // camelCase

    // POST /api/recomendaciones — streaming NDJSON: una recomendación por línea,
    // enriquecida con Jikan, según las va generando el modelo.
    public static void MapRecomendaciones(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/recomendaciones", async (
            PeticionRecomendacion peticion,
            RecomendadorService recomendador,
            JikanService jikan,
            IOptions<ModeloOptions> mOpts,
            IOptions<JikanOptions> jOpts,
            ILoggerFactory loggerFactory,
            HttpContext http,
            CancellationToken ct) =>
        {
            // Saneo de la entrada (acota tamaños y aplana saltos de línea) ANTES de tocar
            // el modelo: sin favoritos válidos no hay nada que recomendar.
            if (peticion.Saneada() is not { } p)
            {
                http.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            var modelo = mOpts.Value;
            await using var e = recomendador.RecomendarStreamAsync(p, ct).GetAsyncEnumerator(ct);

            // El primer MoveNext dispara la llamada al modelo: aquí cazamos los fallos
            // (proveedor caído, timeout) ANTES de escribir el cuerpo, para devolver un código
            // claro. El detalle técnico va al log; al cliente, un mensaje genérico.
            bool hay;
            try
            {
                hay = await e.MoveNextAsync();
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                http.Response.StatusCode = 499;   // el navegador abortó; no hay a quién responder
                return;
            }
            catch (OperationCanceledException)
            {
                await Problema(http, StatusCodes.Status504GatewayTimeout, "El modelo tardó demasiado",
                    $"La generación superó el límite de {modelo.Timeout.TotalSeconds:0} s. Prueba a pedir menos animes.");
                return;
            }
            catch (Exception ex)
            {
                loggerFactory.CreateLogger("Recomendaciones")
                    .LogError(ex, "El modelo ({Proveedor}/{Nombre}) no respondió", modelo.Proveedor, modelo.Nombre);
                await Problema(http, StatusCodes.Status503ServiceUnavailable, "El recomendador no está disponible",
                    "No se pudo generar la recomendación. Inténtalo de nuevo en un momento.");
                return;
            }

            http.Response.ContentType = "application/x-ndjson";
            http.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

            // Para el dedup por sinónimos: no recomendar nada de la misma serie que estos.
            var exclusiones = p.Favoritos
                .Concat(p.Descartados ?? [])
                .Concat(p.Pendientes ?? [])
                .ToArray();

            while (hay)
            {
                // Enriquecemos con Jikan (carátula + ficha) y filtramos alucinaciones y
                // duplicados de serie. Si Jikan está desactivado, pasamos el anime tal cual.
                var anime = jOpts.Value.Habilitado
                    ? await jikan.EnriquecerAsync(e.Current, exclusiones, p.Filtros, ct)
                    : e.Current;

                if (anime is not null)
                {
                    await http.Response.WriteAsync(JsonSerializer.Serialize(anime, JsonWeb) + "\n", ct);
                    await http.Response.Body.FlushAsync(ct);
                }

                try { hay = await e.MoveNextAsync(); }
                catch { break; }   // error o timeout a mitad: cortamos; el cliente conserva lo recibido
            }
        }).RequireRateLimiting("recomendaciones");
    }

    // Escribe un ProblemDetails (mismo formato que Results.Problem) en la respuesta ya iniciada.
    private static async Task Problema(HttpContext http, int status, string title, string detail)
    {
        http.Response.StatusCode = status;
        http.Response.ContentType = "application/problem+json";
        await http.Response.WriteAsJsonAsync(new { title, detail, status });
    }
}

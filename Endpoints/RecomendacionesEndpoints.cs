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
            IOptions<OllamaOptions> oOpts,
            IOptions<JikanOptions> jOpts,
            HttpContext http,
            CancellationToken ct) =>
        {
            var ollama = oOpts.Value;
            await using var e = recomendador.RecomendarStreamAsync(peticion, ct).GetAsyncEnumerator(ct);

            // El primer MoveNext dispara la llamada al modelo: aquí cazamos los fallos
            // (Ollama caído, timeout) ANTES de escribir el cuerpo, para devolver un código claro.
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
                    $"La generación superó el límite de {ollama.Timeout.TotalSeconds:0} s. Prueba con menos animes o un modelo más ligero.");
                return;
            }
            catch (Exception ex)
            {
                await Problema(http, StatusCodes.Status503ServiceUnavailable, "El recomendador no está disponible",
                    $"No se pudo contactar con el modelo en {ollama.Endpoint}. ¿Está 'ollama serve' en marcha y el modelo '{ollama.Model}' descargado? ({ex.Message})");
                return;
            }

            http.Response.ContentType = "application/x-ndjson";
            http.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

            // Para el dedup por sinónimos: no recomendar nada de la misma serie que estos.
            var exclusiones = (peticion.Favoritos ?? [])
                .Concat(peticion.Descartados ?? [])
                .Concat(peticion.Pendientes ?? [])
                .ToArray();

            while (hay)
            {
                // Enriquecemos con Jikan (carátula + ficha) y filtramos alucinaciones y
                // duplicados de serie. Si Jikan está desactivado, pasamos el anime tal cual.
                var anime = jOpts.Value.Habilitado
                    ? await jikan.EnriquecerAsync(e.Current, exclusiones, ct)
                    : e.Current;

                if (anime is not null)
                {
                    await http.Response.WriteAsync(JsonSerializer.Serialize(anime, JsonWeb) + "\n", ct);
                    await http.Response.Body.FlushAsync(ct);
                }

                try { hay = await e.MoveNextAsync(); }
                catch { break; }   // error o timeout a mitad: cortamos; el cliente conserva lo recibido
            }
        });
    }

    // Escribe un ProblemDetails (mismo formato que Results.Problem) en la respuesta ya iniciada.
    private static async Task Problema(HttpContext http, int status, string title, string detail)
    {
        http.Response.StatusCode = status;
        http.Response.ContentType = "application/problem+json";
        await http.Response.WriteAsJsonAsync(new { title, detail, status });
    }
}

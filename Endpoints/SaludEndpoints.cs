using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using AnimeRecommender.Options;

namespace AnimeRecommender.Endpoints;

public static class SaludEndpoints
{
    // GET /api/health — ¿está Ollama levantado y el modelo descargado?
    public static void MapSalud(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/health", async (IHttpClientFactory factory, IOptions<OllamaOptions> oOpts, CancellationToken ct) =>
        {
            var ollama = oOpts.Value;
            var client = factory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(3);
            try
            {
                var tags = await client.GetFromJsonAsync<JsonElement>(
                    $"{ollama.Endpoint.TrimEnd('/')}/api/tags", ct);

                var modelos = tags.TryGetProperty("models", out var arr) && arr.ValueKind == JsonValueKind.Array
                    ? arr.EnumerateArray().Select(m => m.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "")
                    : Enumerable.Empty<string>();

                var disponible = modelos.Any(n =>
                    n.Equals(ollama.Model, StringComparison.OrdinalIgnoreCase) ||
                    n.StartsWith(ollama.Model + ":", StringComparison.OrdinalIgnoreCase));

                return Results.Ok(new
                {
                    ok = true,
                    modelo = ollama.Model,
                    modeloDisponible = disponible,
                    detalle = disponible
                        ? $"Ollama conectado · {ollama.Model}"
                        : $"Ollama conectado, pero el modelo '{ollama.Model}' no está descargado (ollama pull {ollama.Model})."
                });
            }
            catch (Exception ex)
            {
                return Results.Ok(new
                {
                    ok = false,
                    modelo = ollama.Model,
                    modeloDisponible = false,
                    detalle = $"No se pudo contactar con Ollama en {ollama.Endpoint}. ¿Está 'ollama serve' en marcha? ({ex.Message})"
                });
            }
        });
    }
}

using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using AnimeRecommender.Options;

namespace AnimeRecommender.Endpoints;

public static class SaludEndpoints
{
    // GET /api/health — estado del modelo. Con proveedor en la nube no hacemos llamadas
    // remotas (sería gastar cuota en comprobar); la API key ya se validó al arrancar.
    // Con Ollama local sí comprobamos que el servidor responde y el modelo está descargado.
    public static void MapSalud(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/health", async (IHttpClientFactory factory, IOptions<ModeloOptions> mOpts, CancellationToken ct) =>
        {
            var modelo = mOpts.Value;

            if (!modelo.EsLocal)
                return Results.Ok(new
                {
                    ok = true,
                    modelo = modelo.Nombre,
                    modeloDisponible = true,
                    detalle = $"Modelo en la nube · {modelo.Nombre}",
                });

            var client = factory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(3);
            try
            {
                var tags = await client.GetFromJsonAsync<JsonElement>(
                    $"{modelo.Endpoint.TrimEnd('/')}/api/tags", ct);

                var modelos = tags.TryGetProperty("models", out var arr) && arr.ValueKind == JsonValueKind.Array
                    ? arr.EnumerateArray().Select(m => m.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "")
                    : Enumerable.Empty<string>();

                var disponible = modelos.Any(n =>
                    n.Equals(modelo.Nombre, StringComparison.OrdinalIgnoreCase) ||
                    n.StartsWith(modelo.Nombre + ":", StringComparison.OrdinalIgnoreCase));

                return Results.Ok(new
                {
                    ok = true,
                    modelo = modelo.Nombre,
                    modeloDisponible = disponible,
                    detalle = disponible
                        ? $"Modelo local conectado · {modelo.Nombre}"
                        : $"Ollama conectado, pero el modelo '{modelo.Nombre}' no está descargado (ollama pull {modelo.Nombre}).",
                });
            }
            catch
            {
                return Results.Ok(new
                {
                    ok = false,
                    modelo = modelo.Nombre,
                    modeloDisponible = false,
                    detalle = "No se pudo contactar con el modelo local. ¿Está 'ollama serve' en marcha?",
                });
            }
        });
    }
}

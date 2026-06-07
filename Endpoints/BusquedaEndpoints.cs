using AnimeRecommender.Services;

namespace AnimeRecommender.Endpoints;

public static class BusquedaEndpoints
{
    // GET /api/buscar?q= — sugerencias de títulos reales para el autocompletado de favoritos.
    public static void MapBusqueda(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/buscar", async (string? q, JikanService jikan, CancellationToken ct) =>
            Results.Ok(await jikan.BuscarSugerenciasAsync(q ?? "", ct)));
    }
}

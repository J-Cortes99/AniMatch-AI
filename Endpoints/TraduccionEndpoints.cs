using AnimeRecommender.Models;
using AnimeRecommender.Services;

namespace AnimeRecommender.Endpoints;

public static class TraduccionEndpoints
{
    // POST /api/traducir — traduce un texto al español con el modelo local (cacheado).
    public static void MapTraduccion(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/traducir", async (TraduccionPeticion p, TraduccionService traductor, CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(new { traduccion = await traductor.TraducirAsync(p.Texto, ct) });
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return Results.StatusCode(499);
            }
            catch (OperationCanceledException)
            {
                return Results.Problem(title: "La traducción tardó demasiado",
                    statusCode: StatusCodes.Status504GatewayTimeout);
            }
            catch (Exception ex)
            {
                return Results.Problem(title: "No se pudo traducir",
                    detail: $"El modelo no respondió. ({ex.Message})",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        });
    }
}

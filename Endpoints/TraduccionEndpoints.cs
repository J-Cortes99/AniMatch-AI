using Microsoft.Extensions.Options;
using AnimeRecommender.Models;
using AnimeRecommender.Options;
using AnimeRecommender.Services;

namespace AnimeRecommender.Endpoints;

public static class TraduccionEndpoints
{
    // POST /api/traducir — traduce al español la sinopsis del anime indicado por su id
    // de MAL. El servidor resuelve la sinopsis por su cuenta (caché o Jikan); el cliente
    // no puede mandar texto libre al modelo.
    public static void MapTraduccion(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/traducir", async (
            TraduccionPeticion p, TraduccionService traductor, IOptions<JikanOptions> jOpts,
            ILoggerFactory loggerFactory, CancellationToken ct) =>
        {
            if (p.MalId <= 0)
                return Results.BadRequest();

            // Sin Jikan no hay sinopsis que traducir (las fichas llegan sin ella).
            if (!jOpts.Value.Habilitado)
                return Results.NotFound();

            try
            {
                var traduccion = await traductor.TraducirSinopsisAsync(p.MalId, ct);
                return traduccion is null
                    ? Results.NotFound()
                    : Results.Ok(new { traduccion });
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
                loggerFactory.CreateLogger("Traduccion").LogError(ex, "El modelo no respondió");
                return Results.Problem(title: "No se pudo traducir",
                    detail: "El modelo no respondió. Inténtalo de nuevo en un momento.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        }).RequireRateLimiting("traducir");
    }
}

using System.Security.Claims;
using System.Text.Json;
using AnimeRecommender.Services;

namespace AnimeRecommender.Endpoints;

public static class ListasEndpoints
{
    // Cuerpo del PUT: las cuatro colecciones tal cual las maneja el navegador.
    // JsonElement a propósito: el formato lo define el frontend (igual que localStorage);
    // aquí solo validamos forma y tamaño antes de guardarlo como JSONB.
    public record ListasPeticion(
        JsonElement Favoritos, JsonElement Descartados, JsonElement Pendientes, JsonElement Filtros);

    private const int MaxPorLista = 500;          // un usuario legítimo no llega ni de lejos
    private const int MaxLargoJson = 400_000;     // ~400 KB por lista (los objetos llevan sinopsis)

    public static void MapListas(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/listas").RequireAuthorization();

        // GET — las listas guardadas del usuario (vacías si nunca guardó).
        grupo.MapGet("", async (HttpContext http, ListasService listas, CancellationToken ct) =>
        {
            if (!listas.Disponible) return Results.NotFound();
            if (http.User.FindFirstValue(ClaimTypes.NameIdentifier) is not { } id) return Results.Unauthorized();

            var json = await listas.ObtenerAsync(id, ct);
            return json is null
                ? Results.Json(new { favoritos = Array.Empty<object>(), descartados = Array.Empty<object>(), pendientes = Array.Empty<object>(), filtros = new { } })
                : Results.Content(json, "application/json");
        });

        // PUT — guarda todas las listas (el frontend las manda completas y con debounce).
        grupo.MapPut("", async (ListasPeticion p, HttpContext http, ListasService listas, CancellationToken ct) =>
        {
            if (!listas.Disponible) return Results.NotFound();
            if (http.User.FindFirstValue(ClaimTypes.NameIdentifier) is not { } id) return Results.Unauthorized();

            if (!EsLista(p.Favoritos) || !EsLista(p.Descartados) || !EsLista(p.Pendientes) || !EsObjeto(p.Filtros))
                return Results.BadRequest();

            await listas.GuardarAsync(
                id,
                http.User.FindFirstValue(ClaimTypes.Name) ?? "",
                http.User.FindFirstValue(ClaimTypes.Email) ?? "",
                p.Favoritos.GetRawText(), p.Descartados.GetRawText(),
                p.Pendientes.GetRawText(), p.Filtros.GetRawText(),
                ct);
            return Results.Ok();
        });
    }

    private static bool EsLista(JsonElement e) =>
        e.ValueKind == JsonValueKind.Array
        && e.GetArrayLength() <= MaxPorLista
        && e.GetRawText().Length <= MaxLargoJson;

    private static bool EsObjeto(JsonElement e) =>
        e.ValueKind == JsonValueKind.Object && e.GetRawText().Length <= 10_000;
}

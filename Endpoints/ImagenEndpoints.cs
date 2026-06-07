namespace AnimeRecommender.Endpoints;

public static class ImagenEndpoints
{
    // GET /api/img?u= — proxy (mismo origen) de imágenes de MyAnimeList, para poder
    // dibujarlas en un canvas sin "tainting" (CORS) al exportar la página de manga.
    // Restringido al dominio de MAL para no ser un proxy abierto.
    public static void MapImagen(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/img", async (string? u, IHttpClientFactory factory, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(u) || !Uri.TryCreate(u, UriKind.Absolute, out var uri)
                || uri.Scheme != Uri.UriSchemeHttps
                || !(uri.Host == "myanimelist.net" || uri.Host.EndsWith(".myanimelist.net", StringComparison.OrdinalIgnoreCase)))
                return Results.BadRequest();
            try
            {
                var client = factory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(10);
                using var resp = await client.GetAsync(uri, ct);
                if (!resp.IsSuccessStatusCode) return Results.StatusCode((int)resp.StatusCode);
                var bytes = await resp.Content.ReadAsByteArrayAsync(ct);
                return Results.Bytes(bytes, resp.Content.Headers.ContentType?.MediaType ?? "image/jpeg");
            }
            catch { return Results.StatusCode(StatusCodes.Status502BadGateway); }
        });
    }
}

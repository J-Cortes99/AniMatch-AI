using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;
using AnimeRecommender.Models;

namespace AnimeRecommender.Services;

// Enriquece cada recomendación con datos de MyAnimeList (vía la API pública Jikan):
//  - carátula y enlace,
//  - título canónico,
//  - descarta alucinaciones (títulos que no existen en MAL),
//  - descarta lo que sea la misma serie que un favorito/descartado/pendiente (incluye el caso
//    japonés↔inglés, porque comparamos contra TODOS los títulos/sinónimos de la ficha).
//
// Degrada con elegancia: si Jikan falla, devuelve la recomendación sin carátula en vez
// de romper la respuesta. Cachea en memoria y serializa las llamadas con un intervalo
// mínimo para respetar el límite de ritmo de Jikan (~3 req/s).
public sealed class JikanService
{
    private readonly IHttpClientFactory _factory;
    private readonly IMemoryCache _cache;
    private readonly SemaphoreSlim _puerta = new(1, 1);
    private DateTime _ultimaLlamada = DateTime.MinValue;
    private static readonly TimeSpan Intervalo = TimeSpan.FromMilliseconds(400);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

    public JikanService(IHttpClientFactory factory, IMemoryCache cache)
    {
        _factory = factory;
        _cache = cache;
    }

    public async Task<Anime?> EnriquecerAsync(
        Anime anime, IEnumerable<string> exclusiones, CancellationToken ct)
    {
        Ficha? ficha;
        try
        {
            ficha = await BuscarAsync(anime.Titulo, ct);
        }
        catch
        {
            return anime;   // Jikan caído o lento: lo mostramos sin carátula, no rompemos
        }

        if (ficha is null)
            return null;    // no existe en MAL: probable alucinación → fuera

        // Dedup: si cualquier título/sinónimo de la ficha es la misma serie que un
        // favorito, descartado o pendiente, no la recomendamos.
        var titulos = ficha.TodosLosTitulos().ToList();
        foreach (var ex in exclusiones)
            if (titulos.Any(t => MismaSerie(t, ex)))
                return null;

        return anime with
        {
            Titulo = ficha.TitleEnglish ?? ficha.Title ?? anime.Titulo,
            Imagen = ficha.Imagen,
            Url = ficha.Url,
            Nota = ficha.Score,
            Anio = ficha.AnioEstreno,
            Episodios = ficha.Episodes,
            Sinopsis = LimpiarSinopsis(ficha.Synopsis),
            TrailerId = ficha.Trailer?.Id,
            Estudio = ficha.Studios?.FirstOrDefault()?.Name,
        };
    }

    // Autocompletado de favoritos: devuelve unas pocas sugerencias para lo que el usuario escribe.
    public async Task<IReadOnlyList<Sugerencia>> BuscarSugerenciasAsync(string q, CancellationToken ct)
    {
        q = (q ?? "").Trim();
        if (q.Length < 2) return Array.Empty<Sugerencia>();

        var clave = "sug:" + Norm(q);
        if (_cache.TryGetValue(clave, out IReadOnlyList<Sugerencia>? cacheado))
            return cacheado!;

        await _puerta.WaitAsync(ct);
        try
        {
            if (_cache.TryGetValue(clave, out cacheado)) return cacheado!;

            var espera = _ultimaLlamada + Intervalo - DateTime.UtcNow;
            if (espera > TimeSpan.Zero) await Task.Delay(espera, ct);

            IReadOnlyList<Sugerencia> sugerencias;
            try
            {
                var client = _factory.CreateClient("jikan");
                using var resp = await client.GetAsync(
                    $"anime?q={Uri.EscapeDataString(q)}&limit=6&sfw=true", ct);
                _ultimaLlamada = DateTime.UtcNow;
                if (!resp.IsSuccessStatusCode) return Array.Empty<Sugerencia>();

                var datos = await resp.Content.ReadFromJsonAsync<Respuesta>(JsonOpts, ct);
                sugerencias = (datos?.Data ?? new())
                    .Select(f => new Sugerencia(
                        f.TitleEnglish ?? f.Title ?? "",
                        f.AnioEstreno,
                        f.Images?.Jpg?.SmallImageUrl ?? f.Imagen))
                    .Where(s => s.Titulo.Length > 0)
                    .ToList();
            }
            catch
            {
                return Array.Empty<Sugerencia>();   // Jikan caído: sin sugerencias
            }

            _cache.Set(clave, sugerencias, TimeSpan.FromHours(6));
            return sugerencias;
        }
        finally
        {
            _puerta.Release();
        }
    }

    private async Task<Ficha?> BuscarAsync(string titulo, CancellationToken ct)
    {
        var clave = "jikan:" + Norm(titulo);
        if (_cache.TryGetValue(clave, out Resultado? cacheado))
            return cacheado!.Ficha;

        await _puerta.WaitAsync(ct);
        try
        {
            // Por si otro hilo lo cacheó mientras esperábamos la puerta.
            if (_cache.TryGetValue(clave, out cacheado))
                return cacheado!.Ficha;

            var espera = _ultimaLlamada + Intervalo - DateTime.UtcNow;
            if (espera > TimeSpan.Zero) await Task.Delay(espera, ct);

            var client = _factory.CreateClient("jikan");
            using var resp = await client.GetAsync(
                $"anime?q={Uri.EscapeDataString(titulo)}&limit=1", ct);
            _ultimaLlamada = DateTime.UtcNow;

            if (!resp.IsSuccessStatusCode)
                return null;   // 429/5xx: no cacheamos; el siguiente intento podrá reintentar

            var datos = await resp.Content.ReadFromJsonAsync<Respuesta>(JsonOpts, ct);
            var ficha = datos?.Data?.FirstOrDefault();
            _cache.Set(clave, new Resultado(ficha), TimeSpan.FromHours(6));
            return ficha;
        }
        finally
        {
            _puerta.Release();
        }
    }

    // Quita las coletillas de atribución que MAL pega al final de muchas sinopsis:
    // "[Written by MAL Rewrite]", "(Source: Crunchyroll)", "[Source: ...]", etc.
    private static string? LimpiarSinopsis(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return s;
        s = Regex.Replace(s, @"\[\s*(?:written by|source)\b[^\]]*\]", "", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\(\s*(?:written by|source)\b[^)]*\)", "", RegexOptions.IgnoreCase);
        return s.Trim();
    }

    // ---- Comparación de "misma serie" (igual a la del frontend) ----
    private static string Norm(string s) =>
        Regex.Replace((s ?? "").ToLowerInvariant(), @"[^\p{L}\p{N}]+", " ").Trim();

    private static bool MismaSerie(string t1, string t2)
    {
        var a = Norm(t1);
        var b = Norm(t2);
        if (a.Length == 0 || b.Length == 0) return false;
        if (a == b) return true;
        var (corto, largo) = a.Length <= b.Length ? (a, b) : (b, a);
        return corto.Length >= 4 && largo.StartsWith(corto + " ", StringComparison.Ordinal);
    }

    // Cacheamos también los "no encontrado" (Ficha = null) para no repetir búsquedas.
    private sealed record Resultado(Ficha? Ficha);

    // ---- DTOs de la respuesta de Jikan (/v4/anime) ----
    private sealed class Respuesta { public List<Ficha>? Data { get; set; } }

    private sealed class Ficha
    {
        public string? Url { get; set; }
        public string? Title { get; set; }
        public string? TitleEnglish { get; set; }
        public string? TitleJapanese { get; set; }
        public List<Titulo>? Titles { get; set; }
        public Imagenes? Images { get; set; }
        public double? Score { get; set; }
        public int? Episodes { get; set; }
        public int? Year { get; set; }
        public Aired? Aired { get; set; }
        public string? Synopsis { get; set; }
        public Trailer? Trailer { get; set; }
        public List<Studio>? Studios { get; set; }

        public string? Imagen => Images?.Jpg?.ImageUrl;
        // El campo "year" a veces viene null; caemos a la fecha de emisión.
        public int? AnioEstreno => Year ?? Aired?.Prop?.From?.Year;

        public IEnumerable<string> TodosLosTitulos()
        {
            var lista = new List<string?> { Title, TitleEnglish, TitleJapanese };
            if (Titles is not null) lista.AddRange(Titles.Select(t => t.Title));
            return lista.Where(t => !string.IsNullOrWhiteSpace(t))!;
        }
    }

    private sealed class Titulo { public string? Type { get; set; } public string? Title { get; set; } }
    private sealed class Imagenes { public Imagen? Jpg { get; set; } }
    private sealed class Imagen { public string? ImageUrl { get; set; } public string? SmallImageUrl { get; set; } public string? LargeImageUrl { get; set; } }
    private sealed class Aired { public Prop? Prop { get; set; } }
    private sealed class Prop { public Fecha? From { get; set; } }
    private sealed class Fecha { public int? Year { get; set; } public int? Month { get; set; } public int? Day { get; set; } }
    private sealed class Trailer
    {
        public string? YoutubeId { get; set; }
        public string? EmbedUrl { get; set; }
        // A veces youtube_id viene null pero el ID está dentro de embed_url (.../embed/ID?...).
        public string? Id => !string.IsNullOrEmpty(YoutubeId) ? YoutubeId : ExtraerId(EmbedUrl);
        private static string? ExtraerId(string? url)
        {
            if (string.IsNullOrEmpty(url)) return null;
            var m = Regex.Match(url, @"/embed/([A-Za-z0-9_-]{6,})");
            return m.Success ? m.Groups[1].Value : null;
        }
    }
    private sealed class Studio { public string? Name { get; set; } }
}

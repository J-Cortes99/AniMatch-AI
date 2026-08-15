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
        Anime anime, IEnumerable<string> exclusiones, Filtros? filtros, CancellationToken ct)
    {
        var exclusionesList = exclusiones.ToList();
        Ficha? ficha = null;
        try
        {
            ficha = await BuscarAsync(anime.Titulo, ct);
        }
        catch
        {
            // Jikan caído o 504/429/403: probamos AniList como fallback automático
            ficha = await BuscarAniListAsync(anime.Titulo, ct);
        }

        if (ficha is null)
        {
            // Si Jikan devolvió 200 sin resultados, probamos AniList
            ficha = await BuscarAniListAsync(anime.Titulo, ct);
        }

        if (ficha is null)
        {
            // Si ni Jikan ni AniList lo encontraron, devolvemos la recomendación sin carátula
            if (exclusionesList.Any(ex => MismaSerie(anime.Titulo, ex)))
                return null;
            return anime;
        }

        // Dedup: si cualquier título/sinónimo de la ficha es la misma serie que un
        // favorito, descartado o pendiente, no la recomendamos.
        var titulos = ficha.TodosLosTitulos().ToList();
        foreach (var ex in exclusionesList)
            if (titulos.Any(t => MismaSerie(t, ex)))
                return null;

        // Filtro duro contra los datos reales de MAL/AniList + bloqueo de contenido adulto por defecto.
        if (!PasaFiltro(ficha, filtros))
            return null;

        return anime with
        {
            MalId = ficha.MalId,
            Titulo = ficha.TitleEnglish ?? ficha.Title ?? anime.Titulo,
            Imagen = ficha.Imagen,
            Url = ficha.Url,
            Nota = ficha.Score,
            Anio = ficha.AnioEstreno,
            Episodios = ficha.Episodes,
            Sinopsis = LimpiarSinopsis(ficha.Synopsis),
            TrailerId = ficha.TrailerIdDirecto ?? ficha.Trailer?.Id,
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

    // Sinopsis (en inglés, ya limpia) de un anime por su id de MAL. La usa la traducción:
    // así el servidor decide qué texto va al modelo, no el cliente. Null si el anime no
    // existe o no tiene sinopsis.
    public async Task<string?> ObtenerSinopsisAsync(int malId, CancellationToken ct)
    {
        var clave = "sinopsis:" + malId;
        if (_cache.TryGetValue(clave, out string? cacheada))
            return string.IsNullOrEmpty(cacheada) ? null : cacheada;

        await _puerta.WaitAsync(ct);
        try
        {
            if (_cache.TryGetValue(clave, out cacheada))
                return string.IsNullOrEmpty(cacheada) ? null : cacheada;

            var espera = _ultimaLlamada + Intervalo - DateTime.UtcNow;
            if (espera > TimeSpan.Zero) await Task.Delay(espera, ct);

            var client = _factory.CreateClient("jikan");
            using var resp = await client.GetAsync($"anime/{malId}", ct);
            _ultimaLlamada = DateTime.UtcNow;

            // 404 = id que no existe: lo cacheamos (vacío) para no repetir la búsqueda.
            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _cache.Set(clave, "", TimeSpan.FromHours(24));
                return null;
            }
            if (!resp.IsSuccessStatusCode)
                return null;   // 429/5xx: no cacheamos; el siguiente intento podrá reintentar

            // Jikan a veces devuelve 200 con un error envuelto en el cuerpo (sin "data");
            // eso es transitorio: no lo cacheamos, para poder reintentar.
            var datos = await resp.Content.ReadFromJsonAsync<RespuestaUna>(JsonOpts, ct);
            if (datos?.Data is null) return null;

            var sinopsis = LimpiarSinopsis(datos.Data.Synopsis) ?? "";
            _cache.Set(clave, sinopsis, TimeSpan.FromHours(24));
            return sinopsis.Length == 0 ? null : sinopsis;
        }
        finally
        {
            _puerta.Release();
        }
    }

    private DateTime _circuitoAbiertoHasta = DateTime.MinValue;
    private int _consecutivosFallos = 0;

    private async Task<Ficha?> BuscarAsync(string titulo, CancellationToken ct)
    {
        var clave = "jikan:" + Norm(titulo);
        if (_cache.TryGetValue(clave, out Resultado? cacheado))
            return cacheado!.Ficha;

        if (DateTime.UtcNow < _circuitoAbiertoHasta)
            throw new HttpRequestException("Jikan circuito abierto por rate limit");

        await _puerta.WaitAsync(ct);
        try
        {
            if (_cache.TryGetValue(clave, out cacheado))
                return cacheado!.Ficha;

            if (DateTime.UtcNow < _circuitoAbiertoHasta)
                throw new HttpRequestException("Jikan circuito abierto por rate limit");

            var espera = _ultimaLlamada + Intervalo - DateTime.UtcNow;
            if (espera > TimeSpan.Zero) await Task.Delay(espera, ct);

            var client = _factory.CreateClient("jikan");
            using var resp = await client.GetAsync(
                $"anime?q={Uri.EscapeDataString(titulo)}&limit=1", ct);
            _ultimaLlamada = DateTime.UtcNow;

            if (!resp.IsSuccessStatusCode)
            {
                _consecutivosFallos++;
                if (_consecutivosFallos >= 3)
                {
                    _circuitoAbiertoHasta = DateTime.UtcNow.AddSeconds(90);
                    _consecutivosFallos = 0;
                }
                throw new HttpRequestException($"Jikan API devolvió HTTP {(int)resp.StatusCode}");
            }

            _consecutivosFallos = 0;

            var datos = await resp.Content.ReadFromJsonAsync<Respuesta>(JsonOpts, ct);
            var ficha = datos?.Data?.FirstOrDefault();

            // Si no se encuentra con el título exacto y contiene paréntesis (p. ej. " (2011)"),
            // probar una búsqueda alternativa sin el texto entre paréntesis.
            if (ficha is null && titulo.Contains('('))
            {
                var tituloLimpio = Regex.Replace(titulo, @"\s*\([^)]*\)", "").Trim();
                if (tituloLimpio.Length > 0 && !tituloLimpio.Equals(titulo, StringComparison.OrdinalIgnoreCase))
                {
                    espera = _ultimaLlamada + Intervalo - DateTime.UtcNow;
                    if (espera > TimeSpan.Zero) await Task.Delay(espera, ct);

                    using var resp2 = await client.GetAsync(
                        $"anime?q={Uri.EscapeDataString(tituloLimpio)}&limit=1", ct);
                    _ultimaLlamada = DateTime.UtcNow;

                    if (resp2.IsSuccessStatusCode)
                    {
                        var datos2 = await resp2.Content.ReadFromJsonAsync<Respuesta>(JsonOpts, ct);
                        ficha = datos2?.Data?.FirstOrDefault();
                    }
                }
            }

            _cache.Set(clave, new Resultado(ficha), TimeSpan.FromHours(24));
            return ficha;
        }
        catch
        {
            _consecutivosFallos++;
            if (_consecutivosFallos >= 3)
            {
                _circuitoAbiertoHasta = DateTime.UtcNow.AddSeconds(90);
                _consecutivosFallos = 0;
            }
            throw;
        }
        finally
        {
            _puerta.Release();
        }
    }

    private async Task<Ficha?> BuscarAniListAsync(string titulo, CancellationToken ct)
    {
        var clave = "anilist:" + Norm(titulo);
        if (_cache.TryGetValue(clave, out Resultado? cacheado))
            return cacheado!.Ficha;

        try
        {
            var client = _factory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);
            var payload = new
            {
                query = @"
                query ($search: String) {
                  Media (search: $search, type: ANIME) {
                    id
                    siteUrl
                    title { romaji english native }
                    coverImage { large }
                    meanScore
                    episodes
                    seasonYear
                    description(asHtml: false)
                    type
                    genres
                    trailer { id site }
                    studios(isMain: true) { nodes { name } }
                  }
                }",
                variables = new { search = titulo }
            };

            using var resp = await client.PostAsJsonAsync("https://graphql.anilist.co", payload, ct);
            if (!resp.IsSuccessStatusCode) return null;

            var json = await resp.Content.ReadFromJsonAsync<JsonElement>(ct);
            if (!json.TryGetProperty("data", out var data) ||
                !data.TryGetProperty("Media", out var media) ||
                media.ValueKind != JsonValueKind.Object)
                return null;

            var titleObj = media.TryGetProperty("title", out var tObj) ? tObj : default;
            var titleRomaji = titleObj.TryGetProperty("romaji", out var r) ? r.GetString() : null;
            var titleEng = titleObj.TryGetProperty("english", out var e) ? e.GetString() : null;

            var coverObj = media.TryGetProperty("coverImage", out var c) ? c : default;
            var img = coverObj.TryGetProperty("large", out var l) ? l.GetString() : null;

            double? score = media.TryGetProperty("meanScore", out var s) && s.ValueKind == JsonValueKind.Number
                ? Math.Round(s.GetDouble() / 10.0, 1)
                : null;

            int? year = media.TryGetProperty("seasonYear", out var y) && y.ValueKind == JsonValueKind.Number ? y.GetInt32() : null;
            int? ep = media.TryGetProperty("episodes", out var epElem) && epElem.ValueKind == JsonValueKind.Number ? epElem.GetInt32() : null;
            var desc = media.TryGetProperty("description", out var dElem) ? dElem.GetString() : null;
            var url = media.TryGetProperty("siteUrl", out var uElem) ? uElem.GetString() : null;
            var type = media.TryGetProperty("type", out var tElem) ? tElem.GetString() : "TV";

            string? trailerId = null;
            if (media.TryGetProperty("trailer", out var tr) && tr.ValueKind == JsonValueKind.Object)
            {
                var site = tr.TryGetProperty("site", out var st) ? st.GetString() : null;
                if (string.Equals(site, "youtube", StringComparison.OrdinalIgnoreCase) &&
                    tr.TryGetProperty("id", out var trId))
                    trailerId = trId.GetString();
            }

            var studiosList = new List<Studio>();
            if (media.TryGetProperty("studios", out var stObj) &&
                stObj.TryGetProperty("nodes", out var stNodes) &&
                stNodes.ValueKind == JsonValueKind.Array)
            {
                foreach (var stItem in stNodes.EnumerateArray())
                {
                    if (stItem.TryGetProperty("name", out var stName) && stName.GetString() is { Length: > 0 } name)
                        studiosList.Add(new Studio { Name = name });
                }
            }

            var genresList = new List<NombreMal>();
            if (media.TryGetProperty("genres", out var gArr) && gArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var gItem in gArr.EnumerateArray())
                    if (gItem.GetString() is { Length: > 0 } gName)
                        genresList.Add(new NombreMal { Name = gName });
            }

            var ficha = new Ficha
            {
                MalId = media.TryGetProperty("id", out var idElem) ? idElem.GetInt32() : null,
                Title = titleRomaji ?? titleEng ?? titulo,
                TitleEnglish = titleEng,
                Url = url,
                Score = score,
                Episodes = ep,
                Year = year,
                Synopsis = desc,
                Type = type,
                TrailerIdDirecto = trailerId,
                ImagenDirecta = img,
                Studios = studiosList,
                Genres = genresList,
            };

            _cache.Set(clave, new Resultado(ficha), TimeSpan.FromHours(24));
            return ficha;
        }
        catch
        {
            return null;
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

    // ---- Filtro duro contra los datos de MyAnimeList ----
    private static readonly string[] TiposEspeciales = { "OVA", "ONA", "Special", "Music" };

    private static bool PasaFiltro(Ficha ficha, Filtros? f)
    {
        var generos = ficha.GenerosMal().ToList();

        // Bloqueo de contenido adulto SIEMPRE (por defecto): fuera Hentai / Rx.
        if ((ficha.Rating ?? "").StartsWith("Rx", StringComparison.OrdinalIgnoreCase)
            || generos.Any(g => g.Equals("Hentai", StringComparison.OrdinalIgnoreCase)))
            return false;

        if (f is null) return true;

        var tipo = ficha.Type ?? "";

        // A. Formato
        if (f.Formato == "tv" && !tipo.Equals("TV", StringComparison.OrdinalIgnoreCase)) return false;
        if (f.Formato == "pelicula" && !tipo.Equals("Movie", StringComparison.OrdinalIgnoreCase)) return false;
        if (f.SinEspeciales && TiposEspeciales.Contains(tipo, StringComparer.OrdinalIgnoreCase)) return false;

        // B. Géneros excluidos (nombres de MAL en inglés; incluye géneros, temas y demografía)
        if (f.GenerosExcluidos is { Length: > 0 }
            && generos.Any(g => f.GenerosExcluidos.Contains(g, StringComparer.OrdinalIgnoreCase)))
            return false;

        // C. Nota mínima de MAL. Si no tiene nota, no podemos garantizarla → fuera.
        if (f.NotaMinima is > 0 && !(ficha.Score >= f.NotaMinima)) return false;

        // D. Duración por nº de episodios. Si es desconocido, no lo podemos clasificar → fuera.
        if (f.Duracion is "corta" or "media" or "larga")
        {
            if (ficha.Episodes is not { } ep) return false;
            var ok = f.Duracion switch
            {
                "corta" => ep <= 13,
                "media" => ep is > 13 and <= 26,
                "larga" => ep > 26,
                _ => true,
            };
            if (!ok) return false;
        }

        return true;
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
    private sealed class RespuestaUna { public Ficha? Data { get; set; } }   // /v4/anime/{id}

    private sealed class Ficha
    {
        public int? MalId { get; set; }
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
        public string? Type { get; set; }       // TV, Movie, OVA, ONA, Special, Music
        public string? Rating { get; set; }      // "R - 17+…", "Rx - Hentai", etc.
        public List<NombreMal>? Genres { get; set; }
        public List<NombreMal>? Themes { get; set; }
        public List<NombreMal>? Demographics { get; set; }
        public string? ImagenDirecta { get; set; }
        public string? TrailerIdDirecto { get; set; }

        public string? Imagen => ImagenDirecta ?? Images?.Jpg?.ImageUrl;
        // El campo "year" a veces viene null; caemos a la fecha de emisión.
        public int? AnioEstreno => Year ?? Aired?.Prop?.From?.Year;

        public IEnumerable<string> TodosLosTitulos()
        {
            var lista = new List<string?> { Title, TitleEnglish, TitleJapanese };
            if (Titles is not null) lista.AddRange(Titles.Select(t => t.Title));
            return lista.Where(t => !string.IsNullOrWhiteSpace(t))!;
        }

        // Géneros + temas + demografía de MAL (todos cuentan para excluir por "género").
        public IEnumerable<string> GenerosMal()
        {
            var todos = new List<NombreMal>();
            if (Genres is not null) todos.AddRange(Genres);
            if (Themes is not null) todos.AddRange(Themes);
            if (Demographics is not null) todos.AddRange(Demographics);
            return todos.Where(g => !string.IsNullOrWhiteSpace(g.Name)).Select(g => g.Name!);
        }
    }

    private sealed class NombreMal { public string? Name { get; set; } }
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

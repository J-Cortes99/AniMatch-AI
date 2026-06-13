namespace AnimeRecommender.Models;

// Filtros opcionales que aplica el usuario. Se usan de dos formas:
//  1) como PISTA en el prompt, para que el modelo intente cumplirlos (y malgaste menos);
//  2) como filtro DURO contra los datos reales de MyAnimeList tras enriquecer, que es la
//     garantía de verdad (el modelo no conoce notas/tipos exactos de forma fiable).
public record Filtros(
    string? Formato = null,             // "tv" = solo series | "pelicula" = solo películas | null = cualquiera
    bool SinEspeciales = false,         // excluir OVA / ONA / Special / Music
    string[]? GenerosExcluidos = null,  // nombres de género de MAL en inglés (Action, Horror, Ecchi…)
    string[]? GenerosIncluidos = null,  // géneros que el usuario quiere; pista (OR) para el prompt
    double? NotaMinima = null,          // nota mínima de MAL (1-10)
    string? Duracion = null)            // "corta" (≤13) | "media" (14-26) | "larga" (>26), por episodios
{
    // Saneo para el endpoint público: los géneros van interpolados al prompt, así que se
    // acotan en número y longitud. Formato y Duracion no lo necesitan: solo se usan si
    // coinciden exactamente con los valores conocidos.
    public Filtros Saneados() => this with
    {
        GenerosExcluidos = LimpiarGeneros(GenerosExcluidos),
        GenerosIncluidos = LimpiarGeneros(GenerosIncluidos),
        NotaMinima = NotaMinima is { } n && !double.IsNaN(n) ? Math.Clamp(n, 0, 10) : null,
    };

    private static string[] LimpiarGeneros(string[]? generos) =>
        (generos ?? [])
            .Select(g => System.Text.RegularExpressions.Regex.Replace(g ?? "", @"[\s\p{C}]+", " ").Trim())
            .Where(g => g.Length is > 0 and <= 40)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .ToArray();
}

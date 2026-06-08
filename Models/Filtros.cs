namespace AnimeRecommender.Models;

// Filtros opcionales que aplica el usuario. Se usan de dos formas:
//  1) como PISTA en el prompt, para que el modelo intente cumplirlos (y malgaste menos);
//  2) como filtro DURO contra los datos reales de MyAnimeList tras enriquecer, que es la
//     garantía de verdad (el modelo no conoce notas/tipos exactos de forma fiable).
public record Filtros(
    string? Formato = null,             // "tv" = solo series | "pelicula" = solo películas | null = cualquiera
    bool SinEspeciales = false,         // excluir OVA / ONA / Special / Music
    string[]? GenerosExcluidos = null,  // nombres de género de MAL en inglés (Action, Horror, Ecchi…)
    double? NotaMinima = null,          // nota mínima de MAL (1-10)
    string? Duracion = null);           // "corta" (≤13) | "media" (14-26) | "larga" (>26), por episodios

namespace AnimeRecommender.Models;

// Una sugerencia del autocompletado de favoritos (búsqueda en Jikan/MyAnimeList).
public record Sugerencia(string Titulo, int? Anio, string? Imagen);

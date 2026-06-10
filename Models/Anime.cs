namespace AnimeRecommender.Models;

// Una recomendación de anime. El modelo solo produce Titulo/Motivo/Generos; el resto
// (Imagen, Url, Nota, Anio, Episodios, Sinopsis, TrailerId, Estudio) lo rellena Jikan
// al enriquecer, por eso van con valor por defecto.
public record Anime(
    string Titulo, string Motivo, string[] Generos,
    string? Imagen = null, string? Url = null,
    double? Nota = null, int? Anio = null, int? Episodios = null,
    string? Sinopsis = null, string? TrailerId = null, string? Estudio = null,
    int? MalId = null);

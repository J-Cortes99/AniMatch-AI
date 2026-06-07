namespace AnimeRecommender.Options;

// Configuración de la integración con Jikan/MyAnimeList (sección "Jikan").
// Ponlo en false para un modo 100% local (sin carátulas ni llamadas externas).
public sealed class JikanOptions
{
    public bool Habilitado { get; set; } = true;
}

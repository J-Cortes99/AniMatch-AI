namespace AnimeRecommender.Options;

// Configuración del modelo local (sección "Ollama" de appsettings.json).
public sealed class OllamaOptions
{
    public string Endpoint { get; set; } = "http://localhost:11434/";
    public string Model { get; set; } = "gemma3:12b";
    public int TimeoutSeconds { get; set; } = 180;

    public TimeSpan Timeout => TimeSpan.FromSeconds(TimeoutSeconds);
}

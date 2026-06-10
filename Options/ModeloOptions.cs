namespace AnimeRecommender.Options;

// Configuración del modelo de lenguaje (sección "Modelo" de appsettings.json).
// Dos proveedores:
//  - "ollama": modelo local, para desarrollo (appsettings.Development.json).
//  - "gemini": Google Gemini vía su endpoint compatible con OpenAI, para producción.
// La API key NUNCA va en un appsettings versionado: se lee de la variable de entorno
// GEMINI_API_KEY (o Modelo__ApiKey).
public sealed class ModeloOptions
{
    public string Proveedor { get; set; } = "ollama";
    public string Endpoint { get; set; } = "http://localhost:11434/";
    public string Nombre { get; set; } = "gemma3:12b";
    public string? ApiKey { get; set; }
    public int TimeoutSeconds { get; set; } = 60;

    public TimeSpan Timeout => TimeSpan.FromSeconds(TimeoutSeconds);
    public bool EsLocal => Proveedor.Equals("ollama", StringComparison.OrdinalIgnoreCase);
}

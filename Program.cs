using Microsoft.Extensions.AI;
using OllamaSharp;
using AnimeRecommender.Endpoints;
using AnimeRecommender.Options;
using AnimeRecommender.Services;

var builder = WebApplication.CreateBuilder(args);

// --- Configuración tipada (editable en appsettings.json, sin recompilar) ---
builder.Services.Configure<OllamaOptions>(builder.Configuration.GetSection("Ollama"));
builder.Services.Configure<JikanOptions>(builder.Configuration.GetSection("Jikan"));
var ollama = builder.Configuration.GetSection("Ollama").Get<OllamaOptions>() ?? new OllamaOptions();

// --- Modelo de lenguaje (Ollama, local) detrás de IChatClient ---
// Cambiar a un proveedor en la nube (OpenAI-compatible) sería tocar solo esta línea.
builder.Services.AddSingleton<IChatClient>(_ =>
    new OllamaApiClient(new Uri(ollama.Endpoint), ollama.Model));

// --- Jikan / MyAnimeList: enriquecido, búsqueda y proxy de imágenes ---
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient();   // chequeo de salud de Ollama + proxy de imágenes
builder.Services.AddHttpClient("jikan", c =>
{
    c.BaseAddress = new Uri("https://api.jikan.moe/v4/");
    c.Timeout = TimeSpan.FromSeconds(8);
    c.DefaultRequestHeaders.UserAgent.ParseAdd("AniMatch/1.0");
});

// --- Servicios de la aplicación ---
builder.Services.AddSingleton<JikanService>();
builder.Services.AddSingleton<RecomendadorService>();
builder.Services.AddSingleton<TraduccionService>();

var app = builder.Build();

app.UseDefaultFiles();   // sirve wwwroot/index.html en "/"
app.UseStaticFiles();

// --- Endpoints (cada grupo en su archivo, en Endpoints/) ---
app.MapRecomendaciones();
app.MapSalud();
app.MapTraduccion();
app.MapBusqueda();
app.MapImagen();

var serverUrl = builder.Configuration["ServerUrl"] ?? "http://localhost:5080";
app.Run(serverUrl);

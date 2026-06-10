using System.ClientModel;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.AI;
using OllamaSharp;
using OpenAI;
using AnimeRecommender.Endpoints;
using AnimeRecommender.Options;
using AnimeRecommender.Services;

var builder = WebApplication.CreateBuilder(args);

// --- Configuración tipada (editable en appsettings.json, sin recompilar) ---
// La API key del modelo en la nube puede venir de GEMINI_API_KEY (lo habitual en
// hostings) además de Modelo:ApiKey (que cubre la variable Modelo__ApiKey).
if (string.IsNullOrWhiteSpace(builder.Configuration["Modelo:ApiKey"]))
    builder.Configuration["Modelo:ApiKey"] = Environment.GetEnvironmentVariable("GEMINI_API_KEY");

builder.Services.Configure<ModeloOptions>(builder.Configuration.GetSection("Modelo"));
builder.Services.Configure<JikanOptions>(builder.Configuration.GetSection("Jikan"));
var modelo = builder.Configuration.GetSection("Modelo").Get<ModeloOptions>() ?? new ModeloOptions();

// Fallar al arrancar (no en la primera petición) si falta la key del proveedor en la nube.
if (!modelo.EsLocal && string.IsNullOrWhiteSpace(modelo.ApiKey))
    throw new InvalidOperationException(
        $"El proveedor '{modelo.Proveedor}' necesita una API key: define la variable de entorno " +
        "GEMINI_API_KEY (o Modelo__ApiKey), o usa el modelo local con Modelo__Proveedor=ollama.");
var limites = builder.Configuration.GetSection("Limites").Get<LimitesOptions>() ?? new LimitesOptions();

// --- Rate limiting de los endpoints que cuestan dinero (modelo) ---
// Capa 1: límite por IP y minuto (políticas "recomendaciones" y "traducir").
// Capa 2: techo global diario compartido (GlobalLimiter), que acota el gasto máximo
// aunque el abuso venga repartido entre muchas IPs.
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.OnRejected = async (ctx, ct) =>
    {
        if (ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var espera))
            ctx.HttpContext.Response.Headers.RetryAfter = ((int)espera.TotalSeconds).ToString();
        ctx.HttpContext.Response.ContentType = "application/problem+json";
        await ctx.HttpContext.Response.WriteAsJsonAsync(new
        {
            title = "Demasiadas peticiones",
            detail = "Has alcanzado el límite de uso. Espera un poco y vuelve a intentarlo.",
            status = StatusCodes.Status429TooManyRequests,
        }, ct);
    };

    o.AddPolicy("recomendaciones", ctx => PorIp(ctx, "rec", limites.RecomendacionesPorMinutoPorIp));
    o.AddPolicy("traducir", ctx => PorIp(ctx, "trad", limites.TraduccionesPorMinutoPorIp));

    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
        ctx.Request.Path.StartsWithSegments("/api/recomendaciones")
            ? PorDia("rec", limites.RecomendacionesPorDia)
        : ctx.Request.Path.StartsWithSegments("/api/traducir")
            ? PorDia("trad", limites.TraduccionesPorDia)
        : RateLimitPartition.GetNoLimiter("resto"));
});

static RateLimitPartition<string> PorIp(HttpContext ctx, string grupo, int porMinuto) =>
    RateLimitPartition.GetFixedWindowLimiter(
        $"{grupo}:{ctx.Connection.RemoteIpAddress}",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = porMinuto, Window = TimeSpan.FromMinutes(1) });

static RateLimitPartition<string> PorDia(string grupo, int porDia) =>
    RateLimitPartition.GetFixedWindowLimiter(
        "dia:" + grupo,
        _ => new FixedWindowRateLimiterOptions { PermitLimit = porDia, Window = TimeSpan.FromDays(1) });

// --- Cuentas: inicio de sesión con Google (opcional) ---
// Credenciales por entorno: GOOGLE_CLIENT_ID / GOOGLE_CLIENT_SECRET (o Google__ClientId /
// Google__ClientSecret). Sin ellas la app funciona igual, solo que sin login.
if (string.IsNullOrWhiteSpace(builder.Configuration["Google:ClientId"]))
    builder.Configuration["Google:ClientId"] = Environment.GetEnvironmentVariable("GOOGLE_CLIENT_ID");
if (string.IsNullOrWhiteSpace(builder.Configuration["Google:ClientSecret"]))
    builder.Configuration["Google:ClientSecret"] = Environment.GetEnvironmentVariable("GOOGLE_CLIENT_SECRET");
var googleId = builder.Configuration["Google:ClientId"];
var googleSecreto = builder.Configuration["Google:ClientSecret"];
var conGoogle = !string.IsNullOrWhiteSpace(googleId) && !string.IsNullOrWhiteSpace(googleSecreto);

var auth = builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme);
auth.AddCookie(o =>
{
    o.Cookie.Name = "animatch_sesion";
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Lax;
    o.ExpireTimeSpan = TimeSpan.FromDays(30);
    o.SlidingExpiration = true;
    // Somos una API + página estática: ante un 401 no redirigimos a ninguna página de login.
    o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
});
if (conGoogle)
    auth.AddGoogle(o =>
    {
        o.ClientId = googleId!;
        o.ClientSecret = googleSecreto!;
        o.ClaimActions.MapJsonKey("picture", "picture");   // avatar para la cabecera
    });
builder.Services.AddAuthorization();

// Detrás del proxy de un hosting (DetrasDeProxy=true), la IP real del cliente llega en
// X-Forwarded-For. ForwardLimit=1 procesa solo la última entrada (la que añade el proxy
// de confianza), ignorando valores falsificados por el cliente. En local queda apagado:
// si no hay proxy, confiar en esa cabecera permitiría suplantar la IP y esquivar límites.
builder.Services.Configure<ForwardedHeadersOptions>(opts =>
{
    opts.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    opts.ForwardLimit = 1;
    opts.KnownIPNetworks.Clear();
    opts.KnownProxies.Clear();
});

// --- Modelo de lenguaje detrás de IChatClient ---
// "ollama" habla con el servidor local; cualquier otro proveedor (Gemini) usa el
// cliente OpenAI apuntando a su endpoint compatible.
builder.Services.AddSingleton<IChatClient>(_ => modelo.EsLocal
    ? new OllamaApiClient(new Uri(modelo.Endpoint), modelo.Nombre)
    : new OpenAIClient(new ApiKeyCredential(modelo.ApiKey!),
            new OpenAIClientOptions { Endpoint = new Uri(modelo.Endpoint) })
        .GetChatClient(modelo.Nombre)
        .AsIChatClient());

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

// Solo en despliegue con proxy delante (appsettings/variable de entorno DetrasDeProxy=true).
if (builder.Configuration.GetValue("DetrasDeProxy", false))
    app.UseForwardedHeaders();

app.UseDefaultFiles();   // sirve wwwroot/index.html en "/"
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

// --- Endpoints (cada grupo en su archivo, en Endpoints/) ---
app.MapCuenta(conGoogle);
app.MapRecomendaciones();
app.MapSalud();
app.MapTraduccion();
app.MapBusqueda();
app.MapImagen();

var serverUrl = builder.Configuration["ServerUrl"] ?? "http://localhost:5080";
app.Run(serverUrl);

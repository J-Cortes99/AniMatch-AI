namespace AnimeRecommender.Options;

// Límites de uso de los endpoints que llaman al modelo (sección "Limites" de
// appsettings.json). Tres capas:
//  - Con sesión: por usuario y minuto (generoso; dos personas tras el mismo NAT no comparten cupo).
//  - Sin sesión: cupo de prueba por IP y día (suficiente para probar la app; el 429 invita a loguearse).
//  - Techo global diario entre todos: acota el gasto máximo aunque el abuso venga repartido.
public sealed class LimitesOptions
{
    // Con sesión (por usuario y minuto)
    public int RecomendacionesPorMinuto { get; set; } = 6;
    public int TraduccionesPorMinuto { get; set; } = 10;

    // Con sesión (por usuario y día): que un solo usuario no pueda comerse el techo
    // global de todos. Son peticiones, no animes: una tanda gasta 1-3 peticiones,
    // así que 20 ≈ 8-15 tandas reales.
    public int RecomendacionesPorDiaPorUsuario { get; set; } = 20;
    public int TraduccionesPorDiaPorUsuario { get; set; } = 30;

    // Sin sesión (por IP y día) — cada "tanda" del frontend puede hacer 1-3 peticiones
    public int RecomendacionesAnonimasPorDia { get; set; } = 10;
    public int TraduccionesAnonimasPorDia { get; set; } = 10;

    // Techo global diario (todas las IPs y usuarios juntos)
    public int RecomendacionesPorDia { get; set; } = 1000;
    public int TraduccionesPorDia { get; set; } = 2000;
}

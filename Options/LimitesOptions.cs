namespace AnimeRecommender.Options;

// Límites de uso de los endpoints que llaman al modelo (sección "Limites" de
// appsettings.json). Dos capas: por IP y minuto (frena ráfagas de un mismo cliente)
// y por día entre todos los usuarios (techo de gasto aunque el abuso venga
// repartido entre muchas IPs).
public sealed class LimitesOptions
{
    public int RecomendacionesPorMinutoPorIp { get; set; } = 6;
    public int TraduccionesPorMinutoPorIp { get; set; } = 10;
    public int RecomendacionesPorDia { get; set; } = 1000;
    public int TraduccionesPorDia { get; set; } = 2000;
}

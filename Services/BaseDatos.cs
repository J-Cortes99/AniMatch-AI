using Npgsql;

namespace AnimeRecommender.Services;

// Punto único de acceso a Postgres (cadena "AniMatch"). Si no está configurada,
// Disponible=false y los servicios que la usan degradan con elegancia: las listas
// se quedan en localStorage y las traducciones solo en la caché de memoria.
public sealed class BaseDatos : IAsyncDisposable
{
    private readonly NpgsqlDataSource? _bd;

    public bool Disponible => _bd is not null;

    // Solo llamar con Disponible=true.
    public NpgsqlDataSource Fuente => _bd!;

    public BaseDatos(IConfiguration cfg)
    {
        var cadena = cfg.GetConnectionString("AniMatch");
        _bd = string.IsNullOrWhiteSpace(cadena) ? null : NpgsqlDataSource.Create(cadena);
    }

    // Crea las tablas si no existen. Se llama una vez al arrancar.
    public async Task InicializarAsync(CancellationToken ct = default)
    {
        if (_bd is null) return;
        await using var cmd = _bd.CreateCommand("""
            CREATE TABLE IF NOT EXISTS usuarios(
              id          TEXT PRIMARY KEY,            -- 'sub' de Google
              nombre      TEXT NOT NULL DEFAULT '',
              email       TEXT NOT NULL DEFAULT '',
              creado      TIMESTAMPTZ NOT NULL DEFAULT now(),
              actualizado TIMESTAMPTZ NOT NULL DEFAULT now(),
              favoritos   JSONB NOT NULL DEFAULT '[]',
              descartados JSONB NOT NULL DEFAULT '[]',
              pendientes  JSONB NOT NULL DEFAULT '[]',
              filtros     JSONB NOT NULL DEFAULT '{}'
            );
            -- Traducciones de sinopsis: cada una se paga al modelo UNA vez en la vida
            -- de la app (la cache de memoria se pierde con cada redeploy; esta no).
            CREATE TABLE IF NOT EXISTS traducciones(
              mal_id INT PRIMARY KEY,
              texto  TEXT NOT NULL,
              creado TIMESTAMPTZ NOT NULL DEFAULT now()
            );
            """);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public ValueTask DisposeAsync() => _bd?.DisposeAsync() ?? ValueTask.CompletedTask;
}

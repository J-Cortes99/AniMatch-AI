namespace AnimeRecommender.Services;

// Listas por usuario en Postgres: una fila por usuario con las listas como JSONB
// (mismo formato que guarda el navegador en localStorage). Sin base de datos
// configurada, Disponible=false y la app funciona solo con localStorage.
public sealed class ListasService(BaseDatos bd)
{
    public bool Disponible => bd.Disponible;

    // Las listas del usuario como JSON ya montado por Postgres, o null si nunca guardó.
    public async Task<string?> ObtenerAsync(string usuarioId, CancellationToken ct)
    {
        await using var cmd = bd.Fuente.CreateCommand("""
            SELECT json_build_object(
                     'favoritos', favoritos, 'descartados', descartados,
                     'pendientes', pendientes, 'filtros', filtros)::text
            FROM usuarios WHERE id = $1
            """);
        cmd.Parameters.AddWithValue(usuarioId);
        return await cmd.ExecuteScalarAsync(ct) as string;
    }

    // Guarda (upsert) todas las listas del usuario. Los parámetros de listas son JSON
    // ya validado por el endpoint.
    public async Task GuardarAsync(
        string usuarioId, string nombre, string email,
        string favoritos, string descartados, string pendientes, string filtros,
        CancellationToken ct)
    {
        await using var cmd = bd.Fuente.CreateCommand("""
            INSERT INTO usuarios(id, nombre, email, favoritos, descartados, pendientes, filtros)
            VALUES ($1, $2, $3, $4::jsonb, $5::jsonb, $6::jsonb, $7::jsonb)
            ON CONFLICT (id) DO UPDATE SET
              nombre = EXCLUDED.nombre, email = EXCLUDED.email,
              favoritos = EXCLUDED.favoritos, descartados = EXCLUDED.descartados,
              pendientes = EXCLUDED.pendientes, filtros = EXCLUDED.filtros,
              actualizado = now()
            """);
        cmd.Parameters.AddWithValue(usuarioId);
        cmd.Parameters.AddWithValue(nombre);
        cmd.Parameters.AddWithValue(email);
        cmd.Parameters.AddWithValue(favoritos);
        cmd.Parameters.AddWithValue(descartados);
        cmd.Parameters.AddWithValue(pendientes);
        cmd.Parameters.AddWithValue(filtros);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}

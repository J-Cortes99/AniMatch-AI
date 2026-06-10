using System.Text.RegularExpressions;

namespace AnimeRecommender.Models;

// Lo que envía el navegador al pedir recomendaciones. Las listas se usan para que el
// modelo (y el dedup de Jikan) eviten recomendar lo que el usuario ya conoce o ha guardado.
public record PeticionRecomendacion(
    string[] Favoritos,
    string[]? Descartados = null,
    string[]? YaMostrados = null,
    string[]? Pendientes = null,
    int Cantidad = 5,
    Filtros? Filtros = null)
{
    // El endpoint es público y las listas acaban interpoladas en el prompt: acotamos
    // número y longitud (coste de tokens de entrada) y aplanamos saltos de línea y
    // caracteres de control (con los que se cuelan "instrucciones" en los títulos).
    public const int MaxCantidad = 10;
    private const int MaxPorLista = 100;
    private const int MaxLargoTitulo = 150;

    // Copia saneada de la petición, o null si no queda ningún favorito válido (→ 400).
    // De cada lista se conservan los últimos elementos, que son los más recientes.
    public PeticionRecomendacion? Saneada()
    {
        var favoritos = Limpiar(Favoritos);
        if (favoritos.Length == 0) return null;

        return this with
        {
            Favoritos = favoritos,
            Descartados = Limpiar(Descartados),
            YaMostrados = Limpiar(YaMostrados),
            Pendientes = Limpiar(Pendientes),
            Cantidad = Math.Clamp(Cantidad, 1, MaxCantidad),
            Filtros = Filtros?.Saneados(),
        };
    }

    private static string[] Limpiar(string[]? lista) =>
        (lista ?? [])
            .Select(t => Regex.Replace(t ?? "", @"[\s\p{C}]+", " ").Trim())
            .Where(t => t.Length > 0)
            .Select(t => t.Length <= MaxLargoTitulo ? t : t[..MaxLargoTitulo])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .TakeLast(MaxPorLista)
            .ToArray();
}

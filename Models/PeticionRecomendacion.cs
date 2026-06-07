namespace AnimeRecommender.Models;

// Lo que envía el navegador al pedir recomendaciones. Las listas se usan para que el
// modelo (y el dedup de Jikan) eviten recomendar lo que el usuario ya conoce o ha guardado.
public record PeticionRecomendacion(
    string[] Favoritos,
    string[]? Descartados = null,
    string[]? YaMostrados = null,
    string[]? Pendientes = null,
    int Cantidad = 5);

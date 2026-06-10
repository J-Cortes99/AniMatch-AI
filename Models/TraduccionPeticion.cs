namespace AnimeRecommender.Models;

// Petición para traducir la sinopsis de un anime al español. Se envía el id de MAL
// (no el texto): el servidor busca la sinopsis por su cuenta, de modo que el modelo
// solo traduce contenido real de MyAnimeList, nunca texto arbitrario del cliente.
public record TraduccionPeticion(int MalId);

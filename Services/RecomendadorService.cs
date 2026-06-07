using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using AnimeRecommender.Models;
using AnimeRecommender.Options;

namespace AnimeRecommender.Services;

// Construye el prompt y pide recomendaciones al modelo local, devolviéndolas en streaming.
public sealed class RecomendadorService(IChatClient chat, IOptions<OllamaOptions> opciones)
{
    private readonly TimeSpan _timeout = opciones.Value.Timeout;

    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNameCaseInsensitive = true };

    // Pedimos JSON al modelo y vamos extrayendo cada objeto {...} en cuanto se completa,
    // para poder ir emitiendo las recomendaciones según se generan (streaming).
    public async IAsyncEnumerable<Anime> RecomendarStreamAsync(
        PeticionRecomendacion peticion, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var prompt = ConstruirPrompt(peticion);
        var opcionesChat = new ChatOptions { ResponseFormat = ChatResponseFormat.Json };

        // Timeout propio combinado con la cancelación del cliente (si cierra la pestaña).
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_timeout);

        // Escáner incremental: cuenta llaves (ignorando las que van dentro de cadenas)
        // y, al cerrarse CUALQUIER objeto, lo deserializa y lo emite si parece un Anime
        // (tiene "titulo"). Así da igual si el modelo devuelve un array pelado [{...}],
        // lo envuelve en un objeto {"anime":[{...}]} o lo rodea de markdown.
        var buffer = new StringBuilder();
        bool enCadena = false, escape = false;
        var inicios = new Stack<int>();

        await foreach (var update in chat.GetStreamingResponseAsync(prompt, opcionesChat, cts.Token))
        {
            if (string.IsNullOrEmpty(update.Text)) continue;

            foreach (var c in update.Text)
            {
                buffer.Append(c);

                if (escape) { escape = false; continue; }
                if (c == '\\') { escape = true; continue; }
                if (c == '"') { enCadena = !enCadena; continue; }
                if (enCadena) continue;

                if (c == '{')
                {
                    inicios.Push(buffer.Length - 1);
                }
                else if (c == '}' && inicios.Count > 0)
                {
                    var inicio = inicios.Pop();
                    var json = buffer.ToString(inicio, buffer.Length - inicio);

                    Anime? anime = null;
                    try { anime = JsonSerializer.Deserialize<Anime>(json, JsonOpts); }
                    catch (JsonException) { /* objeto con forma rara: lo ignoramos */ }

                    if (anime is { Titulo.Length: > 0 })
                        yield return anime;
                }
            }
        }
    }

    private static string ConstruirPrompt(PeticionRecomendacion peticion)
    {
        var favoritos = string.Join(", ", peticion.Favoritos);

        var descartados = peticion.Descartados is { Length: > 0 }
            ? $"\nEl usuario YA HA VISTO estos y NO le gustan. No los recomiendes JAMÁS: {string.Join(", ", peticion.Descartados)}."
            : "";

        var yaMostrados = peticion.YaMostrados is { Length: > 0 }
            ? $"\nYa le has mostrado estos en esta sesión, no los repitas: {string.Join(", ", peticion.YaMostrados)}."
            : "";

        var pendientes = peticion.Pendientes is { Length: > 0 }
            ? $"\nYa tiene estos guardados para verlos más tarde; tampoco se los recomiendes: {string.Join(", ", peticion.Pendientes)}."
            : "";

        return $$"""
            Eres un recomendador experto de anime.
            Al usuario le gustaron estos animes: {{favoritos}}.{{descartados}}{{yaMostrados}}{{pendientes}}

            Recomiéndale exactamente {{peticion.Cantidad}} animes que probablemente no
            haya visto, explicando por qué encajan con sus gustos.

            NO recomiendes NINGUNO de los animes que ya le gustan, ni los descartados,
            ni los guardados, ni los ya mostrados. Trata como el MISMO anime los títulos equivalentes
            (p. ej. el nombre en japonés y en inglés: "Shingeki no Kyojin" =
            "Attack on Titan"), así como sus secuelas, temporadas y spin-offs.

            En "titulo" usa UN único nombre, el más conocido en español o inglés. NO añadas
            el nombre japonés ni su transliteración (romaji) ni una traducción entre paréntesis
            (p. ej. escribe "My Hero Academia", NO "My Hero Academia (Boku no Hero Academia)").
            Sí puedes conservar entre paréntesis datos que distingan la versión, como el año
            o el nombre de la temporada/ruta (p. ej. "Hunter x Hunter (2011)").

            Dentro del campo "motivo", resalta en negrita con dobles asteriscos de markdown
            los títulos de anime que menciones (p. ej. "Como **Death Note**, explora...").

            Responde ÚNICAMENTE con un array JSON válido, sin envolverlo en bloques de
            código (```) ni añadir texto extra, un objeto por anime con este formato exacto:
            [
              { "titulo": "Nombre del anime", "motivo": "Una frase de por qué le gustará", "generos": ["genero1", "genero2"] }
            ]

            Escribe en español. No inventes animes que no existan.
            """;
    }
}

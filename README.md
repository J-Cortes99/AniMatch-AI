# 🎌 AniMatch

**Recomendador de anime con IA 100% local.** Le dices qué animes te gustan y un modelo de
lenguaje que corre en **tu propia GPU** te sugiere qué ver a continuación —con carátulas, nota,
sinopsis y tráiler de MyAnimeList—, todo sin nube, sin coste y sin enviar tus datos a terceros.

<!-- Sube una captura a docs/ y descomenta esta línea: -->
<!-- ![AniMatch](docs/captura.png) -->

---

## ✨ Características

- 🧠 **Recomendaciones por IA local** vía [Ollama](https://ollama.com) (gemma3) — sin API de pago.
  Hoy corre **en local**, pero la arquitectura (abstracción `IChatClient`) está preparada para
  conmutar a un **servicio de IA en la nube** más adelante, tocando una sola línea.
- ⚡ **Streaming en directo**: las tarjetas van apareciendo según el modelo las genera (NDJSON).
- 🖼️ **Fichas enriquecidas con MyAnimeList** (API pública [Jikan](https://jikan.moe)): carátula,
  nota, año, episodios, estudio, sinopsis y tráiler de YouTube.
- 🚫 **Anti-alucinaciones + dedup por sinónimos**: descarta títulos que no existen y evita
  recomendarte lo que ya conoces, incluido el caso japonés↔inglés ("Shingeki no Kyojin" =
  "Attack on Titan") y sus temporadas/secuelas.
- 🔎 **Autocompletado** de favoritos con títulos reales de MAL.
- ⭐ **Listas personales** persistentes: favoritos, **pendientes** (watchlist) y descartados.
- 🌐 **Traducción de la sinopsis** al español con el propio modelo local, bajo demanda.
- 🖌️ **Exportar la tanda como "página de manga"** en PNG para compartir.
- 🎨 **Interfaz con identidad propia**: estética editorial manga (tinta + bermellón, trama de
  semitono, tipografía de impacto), responsive y con micro-animaciones.

## 🛠️ Stack

| Capa | Tecnología |
|------|-----------|
| Backend | ASP.NET Core **Minimal API** (.NET 10) |
| IA | **Ollama** + `gemma3:12b`, a través de `Microsoft.Extensions.AI` (cambiar de proveedor es **una línea**) |
| Datos | API pública **Jikan** (MyAnimeList) |
| Frontend | Una sola página, **HTML/CSS/JS vanilla** (sin frameworks) |

## 🚀 Cómo ejecutarlo

**Requisitos:** [.NET 10 SDK](https://dotnet.microsoft.com/) y [Ollama](https://ollama.com).
Funciona en cualquier GPU (o CPU, más lento).

```bash
# 1) Arranca Ollama y descarga el modelo
ollama serve
ollama pull gemma3:12b

# 2) Arranca la app
dotnet run
```

Abre **http://localhost:5080** y añade un par de animes que te gusten.

El endpoint, el modelo, el timeout y si usar o no Jikan se configuran en
[`appsettings.json`](appsettings.json) — sin recompilar.

## 🧱 Cómo funciona

1. El navegador envía tus favoritos (+ descartados/pendientes) al backend.
2. `RecomendadorAnime` construye un prompt y pide recomendaciones al modelo local, que
   **devuelve en streaming**; un parser tolerante extrae cada anime en cuanto se completa.
3. Cada recomendación se **enriquece contra MyAnimeList** (carátula, ficha) y se filtran
   alucinaciones y duplicados de serie.
4. El frontend va pintando las tarjetas a medida que llegan (NDJSON) y guarda tus listas en
   `localStorage`.

> La abstracción `IChatClient` de `Microsoft.Extensions.AI` permite cambiar el LLM local por
> uno en la nube (OpenAI-compatible) tocando una sola línea, lo que facilita escalarlo.

## 🗺️ Roadmap

- ☁️ **Servicio de IA en la nube**: dejar el proveedor del LLM configurable (local con Ollama
  **o** un endpoint OpenAI-compatible) para poder desplegarlo multiusuario sin GPU propia.
- 👤 Cuentas de usuario + base de datos para sincronizar listas entre dispositivos.

## 📄 Licencia

MIT — úsalo como quieras.

---

<sub>Proyecto personal. La interfaz, los prompts y el código están en español.</sub>

# 🎌 AniMatch

**Recomendador de anime con IA.** Le dices qué animes te gustan y un modelo de lenguaje te
sugiere qué ver a continuación —con carátulas, nota, sinopsis y tráiler de MyAnimeList—.
El proveedor del modelo es configurable: **Gemini** en la nube para desplegarlo multiusuario,
u **Ollama** en tu propia GPU para desarrollo o uso 100% local.

**🌐 En producción: [animatch.duckdns.org](https://animatch.duckdns.org)** — desplegado con
Docker en una VM ARM de Oracle Cloud, con HTTPS automático (Caddy) y Gemini como modelo.

<!-- Sube una captura a docs/ y descomenta esta línea: -->
<!-- ![AniMatch](docs/captura.png) -->

---

## ✨ Características

- 🧠 **Recomendaciones por IA con proveedor conmutable** (abstracción `IChatClient`):
  **Gemini** (endpoint OpenAI-compatible) para producción, **[Ollama](https://ollama.com)**
  (gemma3) en local para desarrollo — se elige en configuración, sin tocar código.
- 🛡️ **Pensado para exponerlo al público**: rate limiting por IP + techo diario global en los
  endpoints que llaman al modelo, traducción acotada a sinopsis reales de MAL (por id, nunca
  texto libre del cliente) y errores genéricos hacia el cliente (el detalle, al log).
- ⚡ **Streaming en directo**: las tarjetas van apareciendo según el modelo las genera (NDJSON).
- 🖼️ **Fichas enriquecidas con MyAnimeList** (API pública [Jikan](https://jikan.moe)): carátula,
  nota, año, episodios, estudio, sinopsis y tráiler de YouTube.
- 🚫 **Anti-alucinaciones + dedup por sinónimos**: descarta títulos que no existen y evita
  recomendarte lo que ya conoces, incluido el caso japonés↔inglés ("Shingeki no Kyojin" =
  "Attack on Titan") y sus temporadas/secuelas.
- 🔎 **Autocompletado** de favoritos con títulos reales de MAL.
- 👤 **Cuentas con Google** (OAuth en el servidor + cookie de sesión, el navegador nunca ve
  tokens) y **listas sincronizadas entre dispositivos** en Postgres (JSONB, una fila por
  usuario). El primer login migra automáticamente lo que tuvieras en localStorage; sin
  sesión, todo sigue funcionando en local.
- ⭐ **Listas personales** persistentes: favoritos, **pendientes** (watchlist) y descartados.
- 🌐 **Traducción de la sinopsis** al español con el propio modelo, bajo demanda y cacheada.
- 🖌️ **Exportar la tanda como "página de manga"** en PNG para compartir.
- 🎨 **Interfaz con identidad propia**: estética editorial manga (tinta + bermellón, trama de
  semitono, tipografía de impacto), responsive y con micro-animaciones.

## 🛠️ Stack

| Capa | Tecnología |
|------|-----------|
| Backend | ASP.NET Core **Minimal API** (.NET 10) |
| IA | `Microsoft.Extensions.AI` (`IChatClient`) — **Gemini** (`gemini-3.1-flash-lite`) en producción, **Ollama** + `gemma3:12b` en desarrollo |
| Cuentas | OAuth de **Google** en el servidor + cookie de sesión (30 días) |
| Base de datos | **PostgreSQL** (listas por usuario como JSONB; opcional — sin ella, localStorage) |
| Datos de anime | API pública **Jikan** (MyAnimeList) |
| Frontend | Una sola página, **HTML/CSS/JS vanilla** (sin frameworks), responsive |
| Despliegue | **Docker** multi-stage (ARM64) + **Caddy** (HTTPS automático) en una VM Always Free de Oracle Cloud |

## 🚀 Cómo ejecutarlo

**Requisitos:** [.NET 10 SDK](https://dotnet.microsoft.com/), y para el modo de desarrollo
[Ollama](https://ollama.com) (funciona en cualquier GPU; en CPU, más lento).

**Desarrollo (modelo local, sin API key)** — `appsettings.Development.json` ya selecciona Ollama:

```bash
# 1) Arranca Ollama y descarga el modelo
ollama serve
ollama pull gemma3:12b

# 2) Arranca la app
dotnet run
```

**Producción (Gemini en la nube)** — `appsettings.json` ya apunta al endpoint
OpenAI-compatible de Gemini; solo falta la API key
(de [Google AI Studio](https://aistudio.google.com/apikey)), por variable de entorno:

```bash
export GEMINI_API_KEY="tu-clave"        # nunca en appsettings versionado
export ASPNETCORE_ENVIRONMENT=Production
dotnet run --no-launch-profile
```

Abre **http://localhost:5080** y añade un par de animes que te gusten.

**Opcionales por variable de entorno** (sin ellas la app funciona igual, solo que sin
login ni sincronización):

| Variable | Para qué |
|----------|----------|
| `GOOGLE_CLIENT_ID` / `GOOGLE_CLIENT_SECRET` | Login con Google (cliente OAuth de console.cloud.google.com con redirect `…/signin-google`) |
| `ConnectionStrings__AniMatch` (o `ANIMATCH_DB`) | Postgres para las listas por usuario (la tabla se crea sola al arrancar) |

El proveedor/modelo, el timeout, los límites de uso (por IP y diarios), si usar o no Jikan
y el modo proxy (`DetrasDeProxy`) se configuran en [`appsettings.json`](appsettings.json)
— sin recompilar.

## 🐳 Despliegue (producción)

La imagen se construye con el [`Dockerfile`](Dockerfile) multi-stage (funciona en ARM64) y
el [`docker-compose.yml`](docker-compose.yml) está pensado para convivir con otras apps
detrás de un **Caddy** compartido: no publica puertos, se une a la red docker del proxy y
recibe `X-Forwarded-For/Proto` (con `DetrasDeProxy=true`, ya puesto en el compose). Los
secretos van en un `.env` junto al compose (ver [`.env.example`](.env.example)).

```bash
# en el servidor
git clone https://github.com/J-Cortes99/AniMatch-AI.git && cd AniMatch-AI
cp .env.example .env && nano .env     # rellena Gemini, Google y la BD
docker compose up -d --build
```

**CI/CD**: cada push a `main` (salvo cambios solo de documentación) dispara
[`deploy.yml`](.github/workflows/deploy.yml) — buildx construye la imagen ARM64, la sube a
GHCR y un paso SSH actualiza la VM (`compose pull && up -d` + health check). Secretos del
repo: `VM_HOST`, `VM_USER`, `VM_SSH_KEY`. También se puede lanzar a mano desde la pestaña
Actions, y desplegar sin CI con `git pull && docker compose build && docker compose up -d`.

## 🧱 Cómo funciona

1. El navegador envía tus favoritos (+ descartados/pendientes) al backend.
2. `RecomendadorService` construye un prompt y pide recomendaciones al modelo, que
   **devuelve en streaming**; un parser tolerante extrae cada anime en cuanto se completa.
3. Cada recomendación se **enriquece contra MyAnimeList** (carátula, ficha) y se filtran
   alucinaciones y duplicados de serie.
4. El frontend va pintando las tarjetas a medida que llegan (NDJSON) y guarda tus listas en
   `localStorage` — y, con sesión iniciada, las sincroniza con tu cuenta (Postgres) con un
   pequeño debounce.

> La abstracción `IChatClient` de `Microsoft.Extensions.AI` es la que permite que Gemini y
> Ollama sean intercambiables por configuración: el resto del código no sabe cuál hay detrás.

## 🗺️ Roadmap

- 💾 Caché persistente de traducciones (cada sinopsis se paga una sola vez).

## 📄 Licencia

MIT — úsalo como quieras.

---

<sub>Proyecto personal. La interfaz, los prompts y el código están en español.</sub>

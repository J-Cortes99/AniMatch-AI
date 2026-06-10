// Llamadas al backend. pedirStream lee el estado para construir el cuerpo de la petición.
import { favoritos, descartados, pendientes, recomendaciones, filtros } from './estado.js';

// Pasa los filtros de la UI al formato que espera el backend (null = "sin filtrar por esto").
function serializarFiltros() {
  return {
    formato: filtros.formato === 'todo' ? null : filtros.formato,
    sinEspeciales: !!filtros.sinEspeciales,
    generosExcluidos: filtros.generosExcluidos,
    notaMinima: filtros.notaMinima || null,
    duracion: filtros.duracion === 'cualquiera' ? null : filtros.duracion,
  };
}

// GET /api/health → objeto de estado del modelo.
export async function salud() {
  return (await fetch('/api/health')).json();
}

// GET /api/buscar?q= → lista de sugerencias [{titulo, anio, imagen}].
export async function buscar(q) {
  const res = await fetch('/api/buscar?q=' + encodeURIComponent(q));
  if (!res.ok) return [];
  return res.json();
}

// POST /api/traducir → sinopsis del anime (por su id de MAL) traducida al español.
// Lanza si el backend devuelve error.
export async function traducir(malId) {
  const res = await fetch('/api/traducir', {
    method: 'POST', headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ malId })
  });
  if (!res.ok) throw new Error('fallo de traducción');
  const { traduccion } = await res.json();
  return traduccion;
}

// POST /api/recomendaciones (streaming NDJSON): llama onAnime() por cada anime que llega.
// Si onAnime devuelve false, dejamos de leer y cortamos la generación en el servidor.
export async function pedirStream(cantidad, onAnime, signal) {
  const res = await fetch('/api/recomendaciones', {
    method: 'POST', headers: { 'Content-Type': 'application/json' }, signal,
    body: JSON.stringify({
      favoritos,
      descartados: descartados.map(d => d.titulo),
      pendientes: pendientes.map(p => p.titulo),
      yaMostrados: recomendaciones.map(r => r.titulo),
      cantidad,
      filtros: serializarFiltros()
    })
  });
  if (!res.ok) {
    const prob = await res.json().catch(() => null);
    throw new Error(prob?.detail || prob?.title || `Error ${res.status}`);
  }
  const reader = res.body.getReader();
  const dec = new TextDecoder();
  let buf = '', detener = false;
  const procesar = linea => {
    const t = linea.trim();
    if (!t) return;
    let a; try { a = JSON.parse(t); } catch { return; }
    if (a && a.titulo && onAnime(a) === false) detener = true;
  };
  for (;;) {
    const { done, value } = await reader.read();
    if (done) break;
    buf += dec.decode(value, { stream: true });
    let nl;
    while ((nl = buf.indexOf('\n')) >= 0) { procesar(buf.slice(0, nl)); buf = buf.slice(nl + 1); }
    if (detener) { await reader.cancel(); return; }   // ya tenemos los que queríamos: cortamos
  }
  procesar(buf);   // por si quedó algo sin salto de línea final
}

// Estado de la aplicación + persistencia en localStorage. No toca el DOM: las funciones
// solo mutan datos; quien llama se encarga de repintar. Las listas son 'const' (nunca se
// reasignan, solo se mutan con push/splice/length=0) para poder importarse como binding vivo.

const CLAVE_FAV = 'animes_favoritos';
const CLAVE_DESC = 'animes_descartados';
const CLAVE_PEND = 'animes_pendientes';
const CLAVE_FILTROS = 'animes_filtros';

export const favoritos = JSON.parse(localStorage.getItem(CLAVE_FAV) || '[]');
// Descartados y pendientes guardan el objeto anime completo (para verlos con carátula).
// Migramos datos antiguos de descartados que solo guardaban el título.
export const descartados = JSON.parse(localStorage.getItem(CLAVE_DESC) || '[]')
  .map(d => typeof d === 'string' ? { titulo: d } : d);
export const pendientes = JSON.parse(localStorage.getItem(CLAVE_PEND) || '[]');
export const recomendaciones = [];   // lo que hay ahora en pantalla (se limpia con .length = 0)

// Filtros de recomendación (objeto 'const': se mutan sus propiedades, nunca se reasigna).
const FILTROS_DEF = { formato: 'todo', sinEspeciales: false, generosIncluidos: [], generosExcluidos: [], notaMinima: 0, duracion: 'cualquiera' };
export const filtros = Object.assign({}, FILTROS_DEF, JSON.parse(localStorage.getItem(CLAVE_FILTROS) || '{}'));

// Aviso de cambios: app.js registra aquí la subida a la nube (con sesión iniciada).
let alCambiar = null;
export const notificarCambios = fn => { alCambiar = fn; };
const avisar = () => alCambiar?.();

export const guardarFavoritos = () => { localStorage.setItem(CLAVE_FAV, JSON.stringify(favoritos)); avisar(); };
export const guardarDescartados = () => { localStorage.setItem(CLAVE_DESC, JSON.stringify(descartados)); avisar(); };
export const guardarPendientes = () => { localStorage.setItem(CLAVE_PEND, JSON.stringify(pendientes)); avisar(); };
export const guardarFiltros = () => { localStorage.setItem(CLAVE_FILTROS, JSON.stringify(filtros)); avisar(); };

// Sustituye el estado local por lo que llega de la cuenta (y lo persiste en
// localStorage como caché, SIN disparar la subida: acaba de venir de allí).
export function cargarListas(d) {
  favoritos.length = 0; favoritos.push(...(d.favoritos || []));
  descartados.length = 0;
  descartados.push(...(d.descartados || []).map(x => typeof x === 'string' ? { titulo: x } : x));
  pendientes.length = 0; pendientes.push(...(d.pendientes || []));
  Object.assign(filtros, FILTROS_DEF, d.filtros || {});
  localStorage.setItem(CLAVE_FAV, JSON.stringify(favoritos));
  localStorage.setItem(CLAVE_DESC, JSON.stringify(descartados));
  localStorage.setItem(CLAVE_PEND, JSON.stringify(pendientes));
  localStorage.setItem(CLAVE_FILTROS, JSON.stringify(filtros));
}

export const hayDatosLocales = () =>
  favoritos.length > 0 || descartados.length > 0 || pendientes.length > 0;

const norm = t => (t || '').toLowerCase().trim();

// Límites espejo del backend (PeticionRecomendacion.Saneada): el servidor recorta
// igualmente; aquí solo evitamos enviar de más y avisamos al usuario a tiempo.
export const MAX_POR_LISTA = 100;
export const MAX_LARGO_TITULO = 150;

export const yaEsFavorito = titulo => favoritos.some(f => norm(f) === norm(titulo));
export const estaPendiente = titulo => pendientes.some(p => norm(p.titulo) === norm(titulo));

// Añade un favorito (sin duplicados, espacios colapsados, longitud acotada).
// Devuelve true si lo añadió.
export function agregarFavorito(titulo) {
  const t = (titulo || '').replace(/\s+/g, ' ').trim().slice(0, MAX_LARGO_TITULO);
  if (!t || yaEsFavorito(t) || favoritos.length >= MAX_POR_LISTA) return false;
  favoritos.push(t);
  guardarFavoritos();
  return true;
}

// Guarda o quita de pendientes (toggle). Persiste.
export function alternarPendiente(a) {
  const k = pendientes.findIndex(p => norm(p.titulo) === norm(a.titulo));
  if (k >= 0) pendientes.splice(k, 1); else pendientes.push(a);
  guardarPendientes();
}

// ¿Se puede mostrar este título? (no descartado, ni pendiente, ni ya en pantalla)
export function esPermitido(titulo) {
  const prohibidos = new Set([
    ...descartados.map(d => d.titulo),
    ...pendientes.map(p => p.titulo),
    ...recomendaciones.map(r => r.titulo),
  ].map(norm));
  return !prohibidos.has(norm(titulo));
}

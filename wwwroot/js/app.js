// Punto de entrada: estado de UI, render, modales y manejadores de eventos.
import { esc, escNegrita, filaMeta, miniFichaHTML, sello } from './util.js';
import {
  favoritos, descartados, pendientes, recomendaciones, filtros, MAX_POR_LISTA,
  guardarFavoritos, guardarDescartados, guardarPendientes, guardarFiltros,
  agregarFavorito, alternarPendiente, estaPendiente, yaEsFavorito, esPermitido,
  notificarCambios, cargarListas, hayDatosLocales,
} from './estado.js';
import { salud, buscar, traducir, pedirStream, me, salir, obtenerListas, subirListas } from './api.js';
import { exportarPng } from './exportar.js';

const $ = id => document.getElementById(id);

// Estado de la interfaz (escalares; viven aquí porque se reasignan).
let cargandoMsg = null;     // texto de la tarjeta "cargando" (null = ninguna)
let mensajeVacio = null;    // texto cuando no hay resultados (null = ninguno)
let notaFinal = null;       // aviso al pie (p. ej. "con tus filtros solo encontré 3 de 5")
let abortador = null;       // AbortController de la generación en curso (null = nada en curso)
let detalleActual = null;   // anime mostrado ahora en el modal de detalle

// ---- Chips de favoritos ----
function pintarChips() {
  $('chips').innerHTML = favoritos.map((a, i) =>
    `<div class="chip">${esc(a)}<span data-i="${i}">✕</span></div>`).join('');
}
function anadirFavorito(titulo) {
  if (favoritos.length >= MAX_POR_LISTA) { alert(`Máximo ${MAX_POR_LISTA} favoritos.`); return; }
  if (agregarFavorito(titulo)) { pintarChips(); pintarRecomendaciones(); }
}
$('chips').onclick = e => {
  if (e.target.dataset.i !== undefined) {
    favoritos.splice(+e.target.dataset.i, 1); guardarFavoritos();
    pintarChips(); pintarRecomendaciones();   // refresca el +/✓ de las tarjetas
  }
};

// ---- Autocompletado de favoritos (Jikan) ----
let sugLista = [], sugActiva = -1, sugQuery = '', tempBusqueda = null;
function cerrarSugerencias() { $('sugerencias').hidden = true; $('sugerencias').innerHTML = ''; sugLista = []; sugActiva = -1; }
function pintarSugerencias() {
  if (!sugLista.length) { cerrarSugerencias(); return; }
  $('sugerencias').innerHTML = sugLista.map((s, i) => `
    <div class="sug-item${i === sugActiva ? ' activa' : ''}" data-i="${i}">
      ${s.imagen ? `<img class="sug-poster" src="${esc(s.imagen)}" alt="">` : '<span class="sug-poster"></span>'}
      <span class="sug-titulo">${esc(s.titulo)}</span>
      ${s.anio ? `<span class="sug-anio">${s.anio}</span>` : ''}
    </div>`).join('');
  $('sugerencias').hidden = false;
}
async function buscarSugerencias(q) {
  sugQuery = q;
  try {
    const lista = await buscar(q);
    if (sugQuery !== q) return;   // respuesta vieja: la ignoramos
    sugLista = lista; sugActiva = -1;
    pintarSugerencias();
  } catch { /* sin sugerencias */ }
}
$('entrada').oninput = () => {
  clearTimeout(tempBusqueda);
  const q = $('entrada').value.trim();
  if (q.length < 2) { cerrarSugerencias(); return; }
  tempBusqueda = setTimeout(() => buscarSugerencias(q), 250);   // debounce
};
$('entrada').onkeydown = e => {
  const abierto = !$('sugerencias').hidden && sugLista.length > 0;
  if (e.key === 'ArrowDown' && abierto) { e.preventDefault(); sugActiva = (sugActiva + 1) % sugLista.length; pintarSugerencias(); return; }
  if (e.key === 'ArrowUp' && abierto)   { e.preventDefault(); sugActiva = (sugActiva - 1 + sugLista.length) % sugLista.length; pintarSugerencias(); return; }
  if (e.key === 'Escape' && abierto)    { cerrarSugerencias(); return; }
  if (e.key === 'Enter') {
    anadirFavorito(abierto && sugActiva >= 0 ? sugLista[sugActiva].titulo : e.target.value);
    e.target.value = ''; cerrarSugerencias();
  }
};
$('sugerencias').onclick = e => {
  const item = e.target.closest('.sug-item');
  if (!item) return;
  const s = sugLista[+item.dataset.i];
  if (s) { anadirFavorito(s.titulo); $('entrada').value = ''; cerrarSugerencias(); }
};
document.addEventListener('click', e => { if (!e.target.closest('.campo')) cerrarSugerencias(); });

// ---- Cuenta (Google) ----
// La "G" oficial de Google (SVG incrustado: sin peticiones externas).
const LOGO_G = `<svg width="20" height="20" viewBox="0 0 48 48" aria-hidden="true">
  <path fill="#EA4335" d="M24 9.5c3.54 0 6.71 1.22 9.21 3.6l6.85-6.85C35.9 2.38 30.47 0 24 0 14.62 0 6.51 5.38 2.56 13.22l7.98 6.19C12.43 13.72 17.74 9.5 24 9.5z"/>
  <path fill="#4285F4" d="M46.98 24.55c0-1.57-.15-3.09-.38-4.55H24v9.02h12.94c-.58 2.96-2.26 5.48-4.78 7.18l7.73 6c4.51-4.18 7.09-10.36 7.09-17.65z"/>
  <path fill="#FBBC05" d="M10.53 28.59c-.48-1.45-.76-2.99-.76-4.59s.27-3.14.76-4.59l-7.98-6.19C.92 16.46 0 20.12 0 24c0 3.88.92 7.54 2.56 10.78l7.97-6.19z"/>
  <path fill="#34A853" d="M24 48c6.48 0 11.93-2.13 15.89-5.81l-7.73-6c-2.15 1.45-4.92 2.3-8.16 2.3-6.26 0-11.57-4.22-13.47-9.91l-7.98 6.19C6.51 42.62 14.62 48 24 48z"/>
</svg>`;

// Pinta la esquina de sesión: botón de entrar, o avatar + nombre + salir.
// Si el servidor no tiene credenciales de Google, no se muestra nada.
async function pintarCuenta() {
  const cont = $('cuenta');
  try {
    const u = await me();
    if (!u.disponible) { cont.hidden = true; return; }
    cont.hidden = false;
    cont.innerHTML = u.autenticado
      ? `${u.foto ? `<img class="cuenta-foto" src="${esc(u.foto)}" alt="" referrerpolicy="no-referrer">` : ''}
         <span class="cuenta-nombre">${esc(u.nombre)}</span>
         <button class="cuenta-btn" id="btnSalir" type="button">Salir</button>`
      : `<a class="cuenta-google" href="/login">${LOGO_G}<span>Entrar con Google</span></a>`;
    const btn = cont.querySelector('#btnSalir');
    if (btn) btn.onclick = async () => { sesionConListas = false; await salir(); pintarCuenta(); };
    if (u.autenticado && u.listas) await sincronizarListas();
  } catch {
    cont.hidden = true;
  }
}

// ---- Sincronización de listas con la cuenta ----
let sesionConListas = false;   // true = hay sesión y BD: los cambios se suben solos
let tempSubida = null;

// Cada cambio en las listas (con sesión) se sube con un pequeño debounce.
notificarCambios(() => {
  if (!sesionConListas) return;
  clearTimeout(tempSubida);
  tempSubida = setTimeout(() => subirListas().catch(() => {}), 800);
});

// Al iniciar sesión: si la cuenta está vacía y aquí hay datos, se suben (primera vez);
// si la cuenta tiene datos, mandan ellos y se repinta todo.
async function sincronizarListas() {
  try {
    const nube = await obtenerListas();
    const nubeVacia = !(nube.favoritos?.length || nube.descartados?.length || nube.pendientes?.length);
    if (nubeVacia && hayDatosLocales()) {
      await subirListas();
    } else if (!nubeVacia) {
      cargarListas(nube);
      pintarChips(); actualizarContadorDesc(); actualizarContadorPend();
      inicializarFiltros(); pintarRecomendaciones();
    }
    sesionConListas = true;
  } catch { /* sin sincronización: la app sigue funcionando en local */ }
}

// ---- Indicador de estado del modelo ----
async function comprobarEstado() {
  const punto = $('estado').querySelector('.punto');
  const texto = $('estadoTexto');
  punto.style.background = '#5a6072'; texto.textContent = 'Comprobando el modelo…';
  try {
    const s = await salud();
    punto.style.background = !s.ok ? '#ec3b2b' : (s.modeloDisponible ? '#3ec06f' : '#f5a623');
    texto.textContent = s.detalle;
  } catch {
    punto.style.background = '#ec3b2b';
    texto.textContent = 'No se pudo comprobar el estado del modelo.';
  }
}
$('estado').onclick = comprobarEstado;

// ---- Filtros ----
// Catálogo completo de MyAnimeList (géneros + temas + demografía): [etiqueta en español, nombre exacto de MAL].
// El nombre de MAL (en inglés) es lo que entiende el backend; la etiqueta es lo que ve el usuario.
const GENEROS = [
  ['Acción', 'Action'], ['Reparto adulto', 'Adult Cast'], ['Aventura', 'Adventure'],
  ['Antropomórfico', 'Anthropomorphic'], ['Vanguardia', 'Avant Garde'], ['Premiado', 'Award Winning'],
  ['Boys Love (BL)', 'Boys Love'], ['Chicas monas (CGDCT)', 'CGDCT'], ['Cuidado infantil', 'Childcare'],
  ['Deportes de combate', 'Combat Sports'], ['Comedia', 'Comedy'], ['Travestismo', 'Crossdressing'],
  ['Delincuentes', 'Delinquents'], ['Detectives', 'Detective'], ['Drama', 'Drama'], ['Ecchi', 'Ecchi'],
  ['Educativo', 'Educational'], ['Erótico', 'Erotica'], ['Fantasía', 'Fantasy'], ['Humor absurdo', 'Gag Humor'],
  ['Girls Love (Yuri)', 'Girls Love'], ['Gore', 'Gore'], ['Gastronomía', 'Gourmet'], ['Harén', 'Harem'],
  ['Hentai', 'Hentai'], ['Juego de alto riesgo', 'High Stakes Game'], ['Histórico', 'Historical'],
  ['Terror', 'Horror'], ['Idols (chicas)', 'Idols (Female)'], ['Idols (chicos)', 'Idols (Male)'],
  ['Isekai', 'Isekai'], ['Iyashikei (relajante)', 'Iyashikei'], ['Josei', 'Josei'], ['Infantil', 'Kids'],
  ['Triángulo amoroso', 'Love Polygon'], ['Romance estático', 'Love Status Quo'],
  ['Cambio de sexo mágico', 'Magical Sex Shift'], ['Chica mágica (Mahou Shoujo)', 'Mahou Shoujo'],
  ['Artes marciales', 'Martial Arts'], ['Mecha', 'Mecha'], ['Médico', 'Medical'], ['Militar', 'Military'],
  ['Música', 'Music'], ['Misterio', 'Mystery'], ['Mitología', 'Mythology'], ['Crimen organizado', 'Organized Crime'],
  ['Cultura otaku', 'Otaku Culture'], ['Parodia', 'Parody'], ['Artes escénicas', 'Performing Arts'],
  ['Mascotas', 'Pets'], ['Psicológico', 'Psychological'], ['Carreras', 'Racing'], ['Reencarnación', 'Reincarnation'],
  ['Harén inverso', 'Reverse Harem'], ['Romance', 'Romance'], ['Samuráis', 'Samurai'], ['Escolar', 'School'],
  ['Ciencia ficción', 'Sci-Fi'], ['Seinen', 'Seinen'], ['Shoujo', 'Shoujo'], ['Shounen', 'Shounen'],
  ['Mundo del espectáculo', 'Showbiz'], ['Recuentos de la vida', 'Slice of Life'], ['Espacio', 'Space'],
  ['Deportes', 'Sports'], ['Juego de estrategia', 'Strategy Game'], ['Superpoderes', 'Super Power'],
  ['Sobrenatural', 'Supernatural'], ['Supervivencia', 'Survival'], ['Suspense', 'Suspense'],
  ['Deportes de equipo', 'Team Sports'], ['Viajes en el tiempo', 'Time Travel'], ['Fantasía urbana', 'Urban Fantasy'],
  ['Vampiros', 'Vampire'], ['Videojuegos', 'Video Game'], ['Villana', 'Villainess'], ['Artes visuales', 'Visual Arts'],
  ['Trabajo / oficina', 'Workplace'],
];
const ETIQUETA_GEN = Object.fromEntries(GENEROS.map(([es, mal]) => [mal, es]));
const normGen = s => (s || '').toLowerCase().normalize('NFD').replace(/\p{Diacritic}/gu, '').trim();

let genSug = [], genSugActiva = -1;

// Chips de los géneros ya excluidos (tachados, con ✕ para quitar).
function pintarGenerosSel() {
  $('fGenSel').innerHTML = filtros.generosExcluidos.map(mal =>
    `<button type="button" class="f-gen activo" data-mal="${esc(mal)}" title="Quitar exclusión">${esc(ETIQUETA_GEN[mal] || mal)} ✕</button>`
  ).join('');
}
// Sugerencias del buscador: filtra el catálogo por etiqueta o nombre de MAL, sin los ya elegidos.
function pintarGenSug(q) {
  const t = normGen(q);
  genSug = t
    ? GENEROS.filter(([es, mal]) =>
        !filtros.generosExcluidos.includes(mal) &&
        (normGen(es).includes(t) || mal.toLowerCase().includes(t))).slice(0, 8)
    : [];
  if (genSugActiva >= genSug.length) genSugActiva = -1;
  if (!genSug.length) { $('fGenSug').hidden = true; $('fGenSug').innerHTML = ''; return; }
  $('fGenSug').innerHTML = genSug.map(([es, mal], i) =>
    `<div class="f-sug-item${i === genSugActiva ? ' activa' : ''}" data-mal="${esc(mal)}">${esc(es)}</div>`).join('');
  $('fGenSug').hidden = false;
}
function excluirGenero(mal) {
  if (mal && !filtros.generosExcluidos.includes(mal)) filtros.generosExcluidos.push(mal);
  guardarFiltros();
  $('fGenBuscar').value = ''; genSugActiva = -1; pintarGenSug('');
  pintarGenerosSel(); actualizarBotonFiltros();
}
function contarFiltros() {
  return (filtros.formato !== 'todo') + (filtros.duracion !== 'cualquiera')
    + (filtros.notaMinima > 0) + (filtros.sinEspeciales ? 1 : 0) + filtros.generosExcluidos.length;
}
function actualizarBotonFiltros() {
  const n = contarFiltros();
  $('verFiltros').textContent = n ? `Filtros (${n})` : 'Filtros';
  $('verFiltros').classList.toggle('con-filtros', n > 0);
}
function inicializarFiltros() {
  $('fFormato').value = filtros.formato;
  $('fDuracion').value = filtros.duracion;
  $('fNota').value = String(filtros.notaMinima || 0);
  $('fSinEspeciales').checked = !!filtros.sinEspeciales;
  pintarGenerosSel();
  actualizarBotonFiltros();
}
$('verFiltros').onclick = () => {
  const oculto = $('panelFiltros').hidden;
  $('panelFiltros').hidden = !oculto;
  $('verFiltros').setAttribute('aria-expanded', String(oculto));
};
$('fFormato').onchange = e => { filtros.formato = e.target.value; guardarFiltros(); actualizarBotonFiltros(); };
$('fDuracion').onchange = e => { filtros.duracion = e.target.value; guardarFiltros(); actualizarBotonFiltros(); };
$('fNota').onchange = e => { filtros.notaMinima = parseFloat(e.target.value) || 0; guardarFiltros(); actualizarBotonFiltros(); };
$('fSinEspeciales').onchange = e => { filtros.sinEspeciales = e.target.checked; guardarFiltros(); actualizarBotonFiltros(); };

// Buscador de géneros a excluir (autocompletado local sobre el catálogo de MAL).
$('fGenBuscar').oninput = e => { genSugActiva = -1; pintarGenSug(e.target.value); };
$('fGenBuscar').onkeydown = e => {
  if (e.key === 'ArrowDown' && genSug.length) { e.preventDefault(); genSugActiva = (genSugActiva + 1) % genSug.length; pintarGenSug(e.target.value); return; }
  if (e.key === 'ArrowUp' && genSug.length)   { e.preventDefault(); genSugActiva = (genSugActiva - 1 + genSug.length) % genSug.length; pintarGenSug(e.target.value); return; }
  if (e.key === 'Enter') { e.preventDefault(); const p = genSugActiva >= 0 ? genSug[genSugActiva] : genSug[0]; if (p) excluirGenero(p[1]); return; }
  if (e.key === 'Escape') { $('fGenSug').hidden = true; genSugActiva = -1; }
};
$('fGenSug').onclick = e => { const it = e.target.closest('.f-sug-item'); if (it) excluirGenero(it.dataset.mal); };
$('fGenSel').onclick = e => {
  const b = e.target.closest('.f-gen');
  if (!b) return;
  const i = filtros.generosExcluidos.indexOf(b.dataset.mal);
  if (i >= 0) filtros.generosExcluidos.splice(i, 1);
  guardarFiltros(); pintarGenerosSel(); actualizarBotonFiltros(); pintarGenSug($('fGenBuscar').value);
};
document.addEventListener('click', e => { if (!e.target.closest('.f-gen-buscar')) $('fGenSug').hidden = true; });

// ---- Pintar recomendaciones (con tarjeta de "cargando"/"sin resultados" al final) ----
function pintarRecomendaciones() {
  const cards = recomendaciones.map((a, i) => {
    const nueva = i === recomendaciones.length - 1 ? ' nueva' : '';
    const botonPend = estaPendiente(a.titulo)
      ? `<button class="pendiente activo" data-i="${i}" title="En pendientes (quitar)">★</button>`
      : `<button class="pendiente" data-i="${i}" title="Guardar en pendientes (quiero verlo)">☆</button>`;
    const botonFav = yaEsFavorito(a.titulo)
      ? `<button class="favorito" disabled title="Ya está en tus favoritos">✓</button>`
      : `<button class="favorito" data-i="${i}" title="Añadir a mis favoritos"><svg class="ico-mas" viewBox="0 0 24 24" aria-hidden="true"><path d="M12 4v16M4 12h16"/></svg></button>`;
    return `
    <article class="card${nueva}" data-i="${i}" title="Ver ficha">
      <span class="indice">${String(i + 1).padStart(2, '0')}</span>
      ${botonPend}
      ${botonFav}
      <button class="descartar" data-i="${i}" title="Ya lo he visto / no me interesa">✕</button>
      ${a.imagen ? `<img class="poster" src="${esc(a.imagen)}" alt="" loading="lazy">` : ''}
      <div class="card-cuerpo">
        <h3 class="card-titulo">${esc(a.titulo)}</h3>
        ${filaMeta(a)}
        <p class="card-motivo">${escNegrita(a.motivo)}</p>
        <div class="gen">${(a.generos || []).map(g => `<small>${esc(g)}</small>`).join('')}</div>
      </div>
    </article>`;
  }).join('');
  const cargando = cargandoMsg
    ? `<div class="card cargando"><span class="kana">生成中</span> ${esc(cargandoMsg)}</div>` : '';
  const vacio = (!recomendaciones.length && !cargandoMsg && mensajeVacio)
    ? `<div class="card aviso">${esc(mensajeVacio)}</div>` : '';
  const nota = (notaFinal && !cargandoMsg && recomendaciones.length)
    ? `<div class="card aviso">${esc(notaFinal)}</div>` : '';
  $('resultados').innerHTML = cards + cargando + vacio + nota;
  $('exportar').disabled = recomendaciones.length === 0;
}

// Pide recomendaciones hasta tener 'objetivo' en pantalla, reintentando: el modelo
// a veces devuelve menos de lo pedido, y el filtro puede quitar duplicados/descartados.
async function completarHasta(objetivo, mensaje, signal, maxIntentos = 8) {
  const filtrando = contarFiltros() > 0;
  let intentos = 0, vacios = 0;
  while (recomendaciones.length < objetivo && intentos < maxIntentos) {
    cargandoMsg = mensaje;
    pintarRecomendaciones();
    const faltan = objetivo - recomendaciones.length;
    // El filtro duro contra MAL tira muchos candidatos (tipo/nota/duración/género) y el modelo
    // no conoce esos datos con exactitud. Con filtros pedimos un lote MÁS grande para que, tras
    // filtrar, queden N en una o dos rondas en vez de muchas (más rápido y llega a N más veces).
    // Sin filtros basta el colchón pequeño de antes.
    const colchon = filtrando ? Math.max(4, faltan) : ((descartados.length || recomendaciones.length) ? 2 : 0);
    const pedir = Math.min(faltan + colchon, 10);   // 10 = tope de Cantidad en el backend
    let añadidos = 0;
    await pedirStream(pedir, a => {
      if (recomendaciones.length < objetivo && esPermitido(a.titulo)) {
        recomendaciones.push(a); añadidos++;
        if (recomendaciones.length >= objetivo) cargandoMsg = null;  // completos: fuera "Generando…"
        pintarRecomendaciones();
      }
      return recomendaciones.length < objetivo;   // false => ya están todos, paramos el stream
    }, signal);
    intentos++;
    vacios = añadidos === 0 ? vacios + 1 : 0;
    if (vacios >= 2) break;   // dos rondas seguidas sin novedades: el modelo ya no encuentra más
  }
}

function finGeneracion() {
  abortador = null;
  cargandoMsg = null;
  $('cancelar').hidden = true;
  const btn = $('btn'); btn.disabled = false; btn.textContent = 'Recomiéndame';
}

// ---- Botón principal ----
$('btn').onclick = async () => {
  if (!favoritos.length) { alert('Añade al menos un anime.'); return; }
  const btn = $('btn'); btn.disabled = true; btn.textContent = 'Pensando…';
  $('cancelar').hidden = false;
  recomendaciones.length = 0;            // la lista negra (descartados) se mantiene
  mensajeVacio = null; notaFinal = null;
  const objetivo = +$('cantidad').value;
  abortador = new AbortController();
  let cancelado = false;
  try {
    await completarHasta(objetivo, 'Generando recomendaciones…', abortador.signal);
  } catch (err) {
    if (err.name === 'AbortError') {
      cancelado = true;                  // cancelado a propósito: conservamos lo recibido
    } else {
      finGeneracion();
      $('resultados').innerHTML = `<div class="card aviso">Error: ${esc(err.message)}</div>`;
      comprobarEstado();                 // quizá Ollama se cayó: refrescamos el indicador
      return;
    }
  }
  finGeneracion();
  if (!recomendaciones.length)
    mensajeVacio = cancelado
      ? 'Generación cancelada.'
      : 'No encontré recomendaciones nuevas. Prueba a quitar animes de Descartados o a cambiar tus favoritos.';
  else if (!cancelado && recomendaciones.length < objetivo && contarFiltros() > 0)
    notaFinal = `Con tus filtros solo encontré ${recomendaciones.length} de ${objetivo}. Relaja algún filtro para ver más.`;
  pintarRecomendaciones();
};

$('cancelar').onclick = () => { abortador?.abort(); };

// ---- Clic en una tarjeta: favorito (+), pendiente (★), descartar (✕) o abrir ficha ----
$('resultados').onclick = async e => {
  // + Añadir a favoritos
  const fav = e.target.closest('.favorito');
  if (fav && !fav.disabled) {
    const a = recomendaciones[+fav.dataset.i];
    if (a) anadirFavorito(a.titulo);
    return;
  }
  // ★ Guardar/quitar de Pendientes ("quiero verlo")
  const pend = e.target.closest('.pendiente');
  if (pend) {
    const i = +pend.dataset.i;
    const a = recomendaciones[i];
    if (a) {
      const añadiendo = !estaPendiente(a.titulo);   // sello solo al guardar, no al quitar
      alternarPendiente(a);
      actualizarContadorPend();
      pintarRecomendaciones();
      if (añadiendo) sello($('resultados').querySelector(`.card[data-i="${i}"]`));
    }
    return;
  }
  // ✕ Descartar: solo quita la tarjeta (no regenera; pulsa "Recomiéndame" para una tanda nueva)
  const boton = e.target.closest('.descartar');
  if (boton) {
    if (abortador) return;   // ignora descartes mientras hay una generación en curso
    const i = +boton.dataset.i;
    descartados.push(recomendaciones[i]);          // objeto completo (carátula en el modal)
    guardarDescartados();
    actualizarContadorDesc();
    recomendaciones.splice(i, 1);                  // fuera de pantalla, sin sustituto
    pintarRecomendaciones();
    return;
  }
  // Clic en el resto de la tarjeta → abrir la ficha de detalle
  const card = e.target.closest('.card[data-i]');
  if (card) abrirDetalle(recomendaciones[+card.dataset.i]);
};

// ---- Modal de descartados ----
function actualizarContadorDesc() {
  $('verDescartados').textContent = `Descartados (${descartados.length})`;
}
function pintarListaDesc() {
  $('descVacio').hidden = descartados.length > 0;
  $('listaDesc').innerHTML = descartados.map((a, j) => miniFichaHTML(a, j)).join('');
}
$('verDescartados').onclick = () => { pintarListaDesc(); $('overlay').hidden = false; };
$('cerrarModal').onclick = () => { $('overlay').hidden = true; };
$('overlay').onclick = e => { if (e.target === $('overlay')) $('overlay').hidden = true; };
$('listaDesc').onclick = e => {
  const quitar = e.target.closest('.quitar-pend');
  if (quitar) {
    descartados.splice(+quitar.dataset.i, 1);   // vuelve a poder recomendarse
    guardarDescartados(); pintarListaDesc(); actualizarContadorDesc(); pintarRecomendaciones();
    return;
  }
  const item = e.target.closest('.pend-item');
  if (item) abrirDetalle(descartados[+item.dataset.i]);
};

// ---- Pendientes (watchlist) ----
function actualizarContadorPend() {
  $('verPendientes').textContent = `Pendientes (${pendientes.length})`;
}
function pintarListaPend() {
  $('pendVacio').hidden = pendientes.length > 0;
  $('listaPend').innerHTML = pendientes.map((a, j) => miniFichaHTML(a, j)).join('');
}
$('verPendientes').onclick = () => { pintarListaPend(); $('pendientesOverlay').hidden = false; };
$('cerrarPend').onclick = () => { $('pendientesOverlay').hidden = true; };
$('pendientesOverlay').onclick = e => { if (e.target === $('pendientesOverlay')) $('pendientesOverlay').hidden = true; };
$('listaPend').onclick = e => {
  const quitar = e.target.closest('.quitar-pend');
  if (quitar) {
    pendientes.splice(+quitar.dataset.i, 1);
    guardarPendientes(); pintarListaPend(); actualizarContadorPend(); pintarRecomendaciones();
    return;
  }
  const item = e.target.closest('.pend-item');
  if (item) abrirDetalle(pendientes[+item.dataset.i]);
};

// ---- Ficha de detalle ----
// Bloque de sinopsis según el estado: sin traducir (botón) o ya traducida con toggle inglés⇄español.
function sinopsisBloque(a) {
  if (!a.sinopsis) return '';
  let cuerpo, control;
  if (a.sinopsisEs && a.mostrarTraduccion) {
    cuerpo = esc(a.sinopsisEs);
    control = `<button class="link-sinopsis" data-accion="original" type="button">Ver original (inglés)</button>`;
  } else if (a.sinopsisEs) {
    cuerpo = esc(a.sinopsis);
    control = `<button class="link-sinopsis" data-accion="traduccion" type="button">Ver traducción</button>`;
  } else {
    cuerpo = esc(a.sinopsis);
    // La traducción va por id de MAL; fichas antiguas sin malId (guardadas antes
    // de este campo) no la ofrecen.
    control = a.malId
      ? `<button class="btn-traducir" data-accion="traducir" type="button">Traducir al español</button>`
      : '';
  }
  return `<div class="det-bloque" id="bloqueSinopsis"><span class="et">Sinopsis</span><p>${cuerpo}</p>${control}</div>`;
}
function refrescarSinopsis() {
  const bloque = $('detalleBody').querySelector('#bloqueSinopsis');
  if (bloque && detalleActual) bloque.outerHTML = sinopsisBloque(detalleActual);
}
function abrirDetalle(a) {
  if (!a) return;
  detalleActual = a;
  const poster = a.imagen ? `<img class="det-poster" src="${esc(a.imagen)}" alt="">` : '';
  const mal = a.url
    ? `<a class="det-mal" href="${esc(a.url)}" target="_blank" rel="noopener">Ver en MyAnimeList ↗</a>` : '';
  const generos = (a.generos || []).map(g => `<small>${esc(g)}</small>`).join('');
  const trailer = a.trailerId
    ? `<div class="det-bloque"><span class="et">Tráiler</span>
         <button class="yt-facade" data-yt="${esc(a.trailerId)}"
           style="background-image:url('https://img.youtube.com/vi/${esc(a.trailerId)}/hqdefault.jpg')">
           <span class="yt-play">▶</span></button></div>` : '';
  $('detalleBody').innerHTML = `
    <div class="det-cab">
      ${poster}
      <div class="det-info">
        <h2 class="det-titulo">${esc(a.titulo)}</h2>
        ${filaMeta(a, true)}
        <div class="gen">${generos}</div>
        ${mal}
      </div>
    </div>
    <div class="det-bloque"><span class="et">Por qué te lo recomiendo</span><p>${escNegrita(a.motivo)}</p></div>
    ${sinopsisBloque(a)}
    ${trailer}`;
  $('detalleOverlay').hidden = false;
}
function cerrarDetalle() { $('detalleOverlay').hidden = true; $('detalleBody').innerHTML = ''; detalleActual = null; }  // vacía: detiene el tráiler
$('cerrarDetalle').onclick = cerrarDetalle;
$('detalleOverlay').onclick = e => { if (e.target === $('detalleOverlay')) cerrarDetalle(); };
$('detalleBody').onclick = async e => {
  // Carga el tráiler de YouTube solo al pulsar (miniatura → iframe).
  const f = e.target.closest('.yt-facade');
  if (f) {
    const wrap = document.createElement('div');
    wrap.className = 'yt-embed';
    wrap.innerHTML = `<iframe src="https://www.youtube.com/embed/${encodeURIComponent(f.dataset.yt)}?autoplay=1"
      title="Tráiler" allow="autoplay; encrypted-media" allowfullscreen></iframe>`;
    f.replaceWith(wrap);
    return;
  }
  const acc = e.target.closest('[data-accion]');
  if (!acc || !detalleActual) return;
  const a = detalleActual;

  // Alternar entre original (inglés) y traducción (ya cacheada): sin volver a llamar al modelo.
  if (acc.dataset.accion === 'original')   { a.mostrarTraduccion = false; refrescarSinopsis(); return; }
  if (acc.dataset.accion === 'traduccion') { a.mostrarTraduccion = true;  refrescarSinopsis(); return; }

  // Traducir la sinopsis al español (el backend la resuelve por el id de MAL).
  if (acc.dataset.accion === 'traducir') {
    if (!a.malId) return;
    acc.disabled = true; acc.textContent = 'Traduciendo…';
    try {
      a.sinopsisEs = ((await traducir(a.malId)) || '').trim() || a.sinopsis;
      a.mostrarTraduccion = true;
      refrescarSinopsis();
    } catch {
      acc.disabled = false; acc.textContent = 'Reintentar traducción';
    }
  }
};

// Cerrar el modal de arriba con Escape.
document.addEventListener('keydown', e => {
  if (e.key !== 'Escape') return;
  if (!$('detalleOverlay').hidden) cerrarDetalle();
  else if (!$('pendientesOverlay').hidden) $('pendientesOverlay').hidden = true;
  else if (!$('overlay').hidden) $('overlay').hidden = true;
});

$('exportar').onclick = exportarPng;

// ---- Arranque ----
pintarChips();
inicializarFiltros();
actualizarContadorDesc();
actualizarContadorPend();
comprobarEstado();
pintarCuenta();

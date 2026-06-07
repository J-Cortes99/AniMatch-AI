// Punto de entrada: estado de UI, render, modales y manejadores de eventos.
import { esc, escNegrita, filaMeta, miniFichaHTML, sello } from './util.js';
import {
  favoritos, descartados, pendientes, recomendaciones,
  guardarFavoritos, guardarDescartados, guardarPendientes,
  agregarFavorito, alternarPendiente, estaPendiente, yaEsFavorito, esPermitido,
} from './estado.js';
import { salud, buscar, traducir, pedirStream } from './api.js';
import { exportarPng } from './exportar.js';

const $ = id => document.getElementById(id);

// Estado de la interfaz (escalares; viven aquí porque se reasignan).
let cargandoMsg = null;     // texto de la tarjeta "cargando" (null = ninguna)
let mensajeVacio = null;    // texto cuando no hay resultados (null = ninguno)
let abortador = null;       // AbortController de la generación en curso (null = nada en curso)
let detalleActual = null;   // anime mostrado ahora en el modal de detalle
let ocupado = false;

// ---- Chips de favoritos ----
function pintarChips() {
  $('chips').innerHTML = favoritos.map((a, i) =>
    `<div class="chip">${esc(a)}<span data-i="${i}">✕</span></div>`).join('');
}
function anadirFavorito(titulo) {
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

// ---- Indicador de estado de Ollama ----
async function comprobarEstado() {
  const punto = $('estado').querySelector('.punto');
  const texto = $('estadoTexto');
  punto.style.background = '#5a6072'; texto.textContent = 'Comprobando Ollama…';
  try {
    const s = await salud();
    punto.style.background = !s.ok ? '#ec3b2b' : (s.modeloDisponible ? '#3ec06f' : '#f5a623');
    texto.textContent = s.detalle;
  } catch {
    punto.style.background = '#ec3b2b';
    texto.textContent = 'No se pudo comprobar el estado de Ollama.';
  }
}
$('estado').onclick = comprobarEstado;

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
  $('resultados').innerHTML = cards + cargando + vacio;
  $('exportar').disabled = recomendaciones.length === 0;
}

// Pide recomendaciones hasta tener 'objetivo' en pantalla, reintentando: el modelo
// a veces devuelve menos de lo pedido, y el filtro puede quitar duplicados/descartados.
async function completarHasta(objetivo, mensaje, signal, maxIntentos = 6) {
  let intentos = 0, vacios = 0;
  while (recomendaciones.length < objetivo && intentos < maxIntentos) {
    cargandoMsg = mensaje;
    pintarRecomendaciones();
    const faltan = objetivo - recomendaciones.length;
    // Colchón de +2 solo cuando el filtro puede quitar algo (hay descartados o ya hay
    // tarjetas en pantalla); en una primera ronda limpia pedimos justo lo que falta.
    const colchon = (descartados.length || recomendaciones.length) ? 2 : 0;
    let añadidos = 0;
    await pedirStream(faltan + colchon, a => {
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
  mensajeVacio = null;
  abortador = new AbortController();
  let cancelado = false;
  try {
    await completarHasta(+$('cantidad').value, 'Generando recomendaciones…', abortador.signal);
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
  // ✕ Descartar y traer un sustituto
  const boton = e.target.closest('.descartar');
  if (boton) {
    if (ocupado || abortador) return;   // ignora descartes si hay una generación en curso
    ocupado = true;
    const i = +boton.dataset.i;
    descartados.push(recomendaciones[i]);          // objeto completo (carátula en el modal)
    guardarDescartados();
    actualizarContadorDesc();
    const objetivo = recomendaciones.length;       // tras quitar una, volvemos al mismo total
    recomendaciones.splice(i, 1);                  // fuera de pantalla
    pintarRecomendaciones();
    try {
      await completarHasta(objetivo, 'Buscando otra recomendación…');
    } catch (err) {
      console.error(err);
    } finally {
      cargandoMsg = null;
      ocupado = false;
      pintarRecomendaciones();
    }
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
    control = `<button class="btn-traducir" data-accion="traducir" type="button">Traducir al español</button>`;
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

  // Traducir la sinopsis al español con el modelo local (y cachearla en el objeto).
  if (acc.dataset.accion === 'traducir') {
    if (!a.sinopsis) return;
    acc.disabled = true; acc.textContent = 'Traduciendo…';
    try {
      a.sinopsisEs = ((await traducir(a.sinopsis)) || '').trim() || a.sinopsis;
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
actualizarContadorDesc();
actualizarContadorPend();
comprobarEstado();

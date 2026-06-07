// Utilidades de presentación, sin estado de la aplicación.

// Escapa texto para meterlo en HTML de forma segura (el texto del modelo no es de fiar).
export const esc = s => { const d = document.createElement('div'); d.textContent = s ?? ''; return d.innerHTML; };

// Escapa y convierte *texto* o **texto** (markdown del modelo) en negrita.
export const escNegrita = s => esc(s).replace(/\*{1,2}([^*]+)\*{1,2}/g, '<strong>$1</strong>');

// Fila de metadatos (★ nota · año · episodios [· estudio]) — usada en tarjeta y detalle.
export function filaMeta(a, conEstudio = false) {
  const partes = [
    a.nota ? `<span class="nota">★ ${Number(a.nota).toFixed(2)}</span>` : '',
    a.anio ? `<span>${a.anio}</span>` : '',
    a.episodios ? `<span>${a.episodios} ep</span>` : '',
    (conEstudio && a.estudio) ? `<span>${esc(a.estudio)}</span>` : '',
  ].filter(Boolean).join('<span class="sep">·</span>');
  return partes ? `<div class="meta">${partes}</div>` : '';
}

// Mini-ficha para los modales de pendientes y descartados (clic abre detalle, ✕ quita).
export function miniFichaHTML(a, j) {
  return `
    <div class="pend-item" data-i="${j}" title="Ver ficha">
      ${a.imagen ? `<img class="pend-poster" src="${esc(a.imagen)}" alt="" loading="lazy">` : ''}
      <div class="pend-info">
        <div class="pend-titulo">${esc(a.titulo)}</div>
        ${filaMeta(a)}
      </div>
      <button class="quitar-pend" data-i="${j}" title="Quitar">✕</button>
    </div>`;
}

// Animación de sello de tinta sobre una tarjeta (al guardar en pendientes).
export function sello(cardEl) {
  if (!cardEl) return;
  const s = document.createElement('div');
  s.className = 'sello';
  s.innerHTML = '<span>★</span>';
  cardEl.appendChild(s);
  setTimeout(() => s.remove(), 1000);
}

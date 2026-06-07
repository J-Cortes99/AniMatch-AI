// Exporta la tanda actual como una "página de manga" en PNG, dibujada en un canvas.
import { recomendaciones } from './estado.js';

function cargarImagen(url) {
  return new Promise(res => {
    if (!url) { res(null); return; }
    const im = new Image();
    im.onload = () => res(im);
    im.onerror = () => res(null);
    im.src = '/api/img?u=' + encodeURIComponent(url);   // proxy mismo-origen → canvas sin tainting
  });
}

function dibujarCover(ctx, img, dx, dy, dw, dh) {
  const r = Math.max(dw / img.width, dh / img.height);
  const sw = dw / r, sh = dh / r;
  ctx.drawImage(img, (img.width - sw) / 2, (img.height - sh) / 2, sw, sh, dx, dy, dw, dh);
}

function envolver(ctx, texto, x, y, maxW, lh, maxLineas) {
  const palabras = (texto || '').replace(/\*+/g, '').split(/\s+/).filter(Boolean);
  const lineas = []; let linea = '';
  for (const p of palabras) {
    const prueba = linea ? linea + ' ' + p : p;
    if (ctx.measureText(prueba).width > maxW && linea) {
      lineas.push(linea); linea = p;
      if (lineas.length === maxLineas) break;
    } else linea = prueba;
  }
  if (lineas.length < maxLineas && linea) lineas.push(linea);
  const sobran = palabras.join(' ') !== lineas.join(' ');
  if (sobran && lineas.length) {   // recorta la última con elipsis
    let u = lineas[lineas.length - 1];
    while (u && ctx.measureText(u + '…').width > maxW) u = u.slice(0, -1).trimEnd();
    lineas[lineas.length - 1] = u + '…';
  }
  lineas.forEach((l, i) => ctx.fillText(l, x, y + i * lh));
  return y + lineas.length * lh;
}

export async function exportarPng() {
  if (!recomendaciones.length) return;
  const btn = document.getElementById('exportar'), etq = btn.textContent;
  btn.disabled = true; btn.textContent = 'Generando…';
  try {
    await Promise.all([
      document.fonts.load('400 64px "Anton"'),
      document.fonts.load('800 26px "Shippori Mincho"'),
      document.fonts.load('400 15px "Zen Kaku Gothic New"'),
    ]).catch(() => {});
    const imgs = await Promise.all(recomendaciones.map(r => cargarImagen(r.imagen)));

    const S = 2, W = 820, pad = 34, headH = 150, panelH = 196, gap = 16, footH = 56;
    const H = headH + recomendaciones.length * (panelH + gap) + footH;
    const c = document.createElement('canvas');
    c.width = W * S; c.height = H * S;
    const ctx = c.getContext('2d'); ctx.scale(S, S); ctx.textBaseline = 'alphabetic';

    ctx.fillStyle = '#0d0b09'; ctx.fillRect(0, 0, W, H);
    ctx.fillStyle = 'rgba(239,231,212,0.05)';
    for (let y = 0; y < H; y += 7) for (let x = 0; x < W; x += 7) { ctx.beginPath(); ctx.arc(x, y, 0.8, 0, 6.3); ctx.fill(); }
    const glow = ctx.createRadialGradient(W * 0.85, -40, 30, W * 0.85, -40, 440);
    glow.addColorStop(0, 'rgba(236,59,43,0.18)'); glow.addColorStop(1, 'rgba(236,59,43,0)');
    ctx.fillStyle = glow; ctx.fillRect(0, 0, W, 320);

    // Cabecera
    ctx.fillStyle = '#ec3b2b'; ctx.font = '400 13px Anton, sans-serif';
    ctx.fillText('ローカルAI   ·   RECOMENDADOR DE ANIME', pad, 46);
    ctx.fillStyle = '#efe7d4'; ctx.font = '400 64px Anton, sans-serif';
    ctx.fillText('ANIMATCH', pad - 2, 112);
    ctx.fillStyle = '#ec3b2b'; ctx.fillRect(pad, 124, 150, 5);
    ctx.fillStyle = '#8b8270'; ctx.font = '400 14px "Zen Kaku Gothic New", sans-serif';
    ctx.textAlign = 'right'; ctx.fillText(new Date().toLocaleDateString('es-ES'), W - pad, 112); ctx.textAlign = 'left';

    // Paneles
    recomendaciones.forEach((a, i) => {
      const y = headH + i * (panelH + gap), x = pad, w = W - pad * 2, h = panelH;
      ctx.fillStyle = '#15120d'; ctx.fillRect(x, y, w, h);
      ctx.strokeStyle = '#332c20'; ctx.lineWidth = 2; ctx.strokeRect(x + 1, y + 1, w - 2, h - 2);
      ctx.fillStyle = 'rgba(239,231,212,0.05)'; ctx.font = '400 116px Anton, sans-serif';
      ctx.textAlign = 'right'; ctx.fillText(String(i + 1).padStart(2, '0'), x + w - 12, y + h - 8); ctx.textAlign = 'left';

      const pw = 116, ph = h - 24, px = x + 16, py = y + 12;
      ctx.fillStyle = '#1d1812'; ctx.fillRect(px, py, pw, ph);
      if (imgs[i]) dibujarCover(ctx, imgs[i], px, py, pw, ph);
      ctx.strokeStyle = 'rgba(13,11,9,0.6)'; ctx.lineWidth = 1.5; ctx.strokeRect(px, py, pw, ph);

      const tx = px + pw + 20, tw = x + w - tx - 28;
      let ty = y + 38;
      ctx.fillStyle = '#efe7d4'; ctx.font = '800 26px "Shippori Mincho", serif';
      ty = envolver(ctx, a.titulo, tx, ty, tw, 30, 2) + 6;

      ctx.font = '400 15px Anton, sans-serif';
      let mx = tx;
      if (a.nota) { ctx.fillStyle = '#ec3b2b'; ctx.fillText('★ ', mx, ty); mx += ctx.measureText('★ ').width; }
      const partes = [];
      if (a.nota) partes.push(Number(a.nota).toFixed(2));
      if (a.anio) partes.push(String(a.anio));
      if (a.episodios) partes.push(a.episodios + ' ep');
      if (a.estudio) partes.push(a.estudio);
      ctx.fillStyle = '#c8bea6'; ctx.fillText(partes.join('   ·   '), mx, ty);
      ty += 24;

      ctx.fillStyle = '#c8bea6'; ctx.font = '400 15px "Zen Kaku Gothic New", sans-serif';
      ty = envolver(ctx, a.motivo, tx, ty, tw, 21, 4) + 8;
      ctx.fillStyle = '#8b8270'; ctx.font = '400 12px Anton, sans-serif';
      ctx.fillText((a.generos || []).join('   /   ').toUpperCase(), tx, Math.min(ty, y + h - 14));
    });

    // Pie
    ctx.fillStyle = '#8b8270'; ctx.font = '400 13px "Zen Kaku Gothic New", sans-serif';
    ctx.textAlign = 'center';
    ctx.fillText('Generado en local con AniMatch   ·   datos y carátulas de MyAnimeList', W / 2, H - 22);
    ctx.textAlign = 'left';

    const blob = await new Promise(r => c.toBlob(r, 'image/png'));
    const url = URL.createObjectURL(blob);
    const enlace = document.createElement('a');
    enlace.href = url; enlace.download = 'animatch.png'; enlace.click();
    URL.revokeObjectURL(url);
  } catch (err) {
    console.error(err); alert('No se pudo generar el PNG.');
  } finally {
    btn.textContent = etq; btn.disabled = recomendaciones.length === 0;
  }
}

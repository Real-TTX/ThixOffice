// Renders the logos of every app of ThixOffice (branding/<id>/*.svg) into every file the apps, the packages and
// ThixLauncher need, with the installed Microsoft Edge (headless).
//
//   cd tools && npm install && node render-icons.js [--preview <file.png>]
//
// Detail levels per app (see branding/README.md):
//   16-32 px   logo-small.svg           full-bleed tile, the glyph reduced to its few strong shapes
//   48+ px     logo-icon.svg            the tile with the whole glyph
//   document   logo-document.svg        the file icon of the app's documents (logo-document-small.svg at 16-32 px if present)
//   full       logo-full.svg            icon + word mark "Thix<Suffix>"
// Output per app <id> (thixwrite, thixcalc, thixshow):
//   branding/<id>/png/<id>-<size>.png            app icon 16 … 1024
//   branding/<id>/png/<id>-mac-<size>.png        macOS app icon with Apple's margin (scripts/package-macos.sh builds the .icns)
//   branding/<id>/png/<id>-document-<size>.png   document icon 16 … 1024 (and -document-mac-<size> for the .icns)
//   branding/<id>/png/<id>-full.png              logo with word mark
//   branding/<id>/<id>.ico, <id>-document.ico    Windows icons (16-256)
//   branding/launcher/<id>.png                   256 px tile for ThixLauncher (its Assets/apps/<id>.png)
//   src/<Name>/Assets/{<id>.ico, <id>-document.ico, logo-256.png, logo-64.png}   when the app's project exists
// --preview <file>: one sheet with every app at 16, 20, 24, 32, 48, 64 and 256 px on light and dark, to LOOK at.
'use strict';

const fs = require('fs');
const path = require('path');
const { chromium } = require('playwright-core');

const root = path.resolve(__dirname, '..');
const branding = path.join(root, 'branding');
const apps = [
  { id: 'thixwrite', name: 'ThixWrite' },
  { id: 'thixcalc', name: 'ThixCalc' },
  { id: 'thixshow', name: 'ThixShow' },
].filter((app) => fs.existsSync(path.join(branding, app.id, 'logo-icon.svg')));

const read = (app, name) => {
  const file = path.join(branding, app.id, name);
  return fs.existsSync(file) ? fs.readFileSync(file, 'utf8') : null;
};

// An SVG drawn at a size, optionally scaled inside the canvas (macOS icons keep a margin around the tile).
function sized(source, size) {
  return source
    .replace(/<svg([^>]*?)\swidth="[^"]*"/, '<svg$1')
    .replace(/<svg([^>]*?)\sheight="[^"]*"/, '<svg$1')
    .replace('<svg', `<svg width="${size}" height="${size}"`);
}

function page(source, size, scale = 1) {
  const inner = Math.round(size * scale);
  const offset = (size - inner) / 2;
  return `<!doctype html><html><head><style>html,body{margin:0;padding:0;background:transparent}` +
    `div{position:absolute;left:${offset}px;top:${offset}px;width:${inner}px;height:${inner}px}svg{display:block}</style></head>` +
    `<body><div>${sized(source, inner)}</div></body></html>`;
}

async function render(browser, source, size, scale = 1) {
  const context = await browser.newContext({ viewport: { width: size, height: size }, deviceScaleFactor: 1 });
  const tab = await context.newPage();
  await tab.setContent(page(source, size, scale));
  const bytes = await tab.screenshot({ omitBackground: true, clip: { x: 0, y: 0, width: size, height: size } });
  await context.close();
  return bytes;
}

async function renderHtml(browser, html, width, height, transparent = true) {
  const context = await browser.newContext({ viewport: { width, height }, deviceScaleFactor: 1 });
  const tab = await context.newPage();
  await tab.setContent(html);
  await tab.waitForTimeout(100);
  const bytes = await tab.screenshot({ omitBackground: transparent, clip: { x: 0, y: 0, width, height } });
  await context.close();
  return bytes;
}

// ICO with PNG entries (Windows Vista and later).
function ico(images) {
  const header = Buffer.alloc(6 + 16 * images.length);
  header.writeUInt16LE(0, 0);
  header.writeUInt16LE(1, 2);
  header.writeUInt16LE(images.length, 4);
  let offset = header.length;
  images.forEach(({ size, bytes }, i) => {
    const at = 6 + 16 * i;
    header.writeUInt8(size >= 256 ? 0 : size, at);
    header.writeUInt8(size >= 256 ? 0 : size, at + 1);
    header.writeUInt8(0, at + 2);
    header.writeUInt8(0, at + 3);
    header.writeUInt16LE(1, at + 4);
    header.writeUInt16LE(32, at + 6);
    header.writeUInt32LE(bytes.length, at + 8);
    header.writeUInt32LE(offset, at + 12);
    offset += bytes.length;
  });
  return Buffer.concat([header, ...images.map((image) => image.bytes)]);
}

const appSizes = [16, 20, 24, 32, 40, 48, 64, 128, 256, 512, 1024];
const macSizes = [16, 32, 64, 128, 256, 512, 1024];
const icoSizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];

async function renderApp(browser, app) {
  const pngDir = path.join(branding, app.id, 'png');
  fs.mkdirSync(pngDir, { recursive: true });
  const icon = read(app, 'logo-icon.svg');
  const small = read(app, 'logo-small.svg') ?? icon;
  const documentIcon = read(app, 'logo-document.svg');
  const documentSmall = read(app, 'logo-document-small.svg') ?? documentIcon;
  const forSize = (size) => (size <= 32 ? small : icon);
  const documentForSize = (size) => (size <= 32 ? documentSmall : documentIcon);

  const bySize = {};
  for (const size of appSizes) {
    bySize[size] = await render(browser, forSize(size), size);
    fs.writeFileSync(path.join(pngDir, `${app.id}-${size}.png`), bySize[size]);
  }

  for (const size of macSizes) {
    fs.writeFileSync(path.join(pngDir, `${app.id}-mac-${size}.png`), await render(browser, forSize(size), size, 0.92));
  }

  const documentBySize = {};
  if (documentIcon) {
    for (const size of appSizes) {
      documentBySize[size] = await render(browser, documentForSize(size), size);
      fs.writeFileSync(path.join(pngDir, `${app.id}-document-${size}.png`), documentBySize[size]);
    }

    for (const size of macSizes) {
      fs.writeFileSync(path.join(pngDir, `${app.id}-document-mac-${size}.png`), await render(browser, documentForSize(size), size, 0.92));
    }
  }

  const full = read(app, 'logo-full.svg');
  if (full) {
    fs.writeFileSync(path.join(pngDir, `${app.id}-full.png`),
      await renderHtml(browser, `<!doctype html><html><body style="margin:0;background:transparent">${full}</body></html>`, 900, 200));
  }

  const appIco = ico(icoSizes.map((size) => ({ size, bytes: bySize[size] })));
  fs.writeFileSync(path.join(branding, app.id, `${app.id}.ico`), appIco);
  const documentIco = documentIcon ? ico(icoSizes.map((size) => ({ size, bytes: documentBySize[size] }))) : null;
  if (documentIco) {
    fs.writeFileSync(path.join(branding, app.id, `${app.id}-document.ico`), documentIco);
  }

  const launcher = path.join(branding, 'launcher');
  fs.mkdirSync(launcher, { recursive: true });
  fs.writeFileSync(path.join(launcher, `${app.id}.png`), bySize[256]);

  // The app's own assets, when its project exists.
  const assets = path.join(root, 'src', app.name, 'Assets');
  if (fs.existsSync(path.join(root, 'src', app.name))) {
    fs.mkdirSync(assets, { recursive: true });
    fs.writeFileSync(path.join(assets, `${app.id}.ico`), appIco);
    if (documentIco) {
      fs.writeFileSync(path.join(assets, `${app.id}-document.ico`), documentIco);
    }

    fs.writeFileSync(path.join(assets, 'logo-256.png'), bySize[256]);
    fs.writeFileSync(path.join(assets, 'logo-64.png'), bySize[64]);
  }

  console.log(`${app.name}: written to ${path.relative(root, pngDir)}${fs.existsSync(assets) ? ` and ${path.relative(root, assets)}` : ''}`);
}

// One sheet: every app at the sizes that matter, on a light and a dark surface, with its document icon and word mark.
async function preview(browser, target) {
  const sizes = [16, 20, 24, 32, 48, 64, 256];
  let instance = 0;
  // Every SVG of the sheet gets its own ids: gradients of the same name in one page would all take the first one.
  const unique = (source) => {
    instance += 1;
    return source.replace(/id="([^"]+)"/g, `id="p${instance}-$1"`).replace(/url\(#([^)]+)\)/g,`url(#p${instance}-$1)`);
  };
  const cell = (source, size) => `<div class="c"><div style="width:${size}px;height:${size}px">${unique(sized(source, size))}</div><span>${size}</span></div>`;
  const row = (app, background, color) => {
    const icon = read(app, 'logo-icon.svg');
    const small = read(app, 'logo-small.svg') ?? icon;
    const documentIcon = read(app, 'logo-document.svg');
    const documentSmall = read(app, 'logo-document-small.svg') ?? documentIcon;
    const icons = sizes.map((size) => cell(size <= 32 ? small : icon, size)).join('');
    const documents = documentIcon ? sizes.filter((size) => size <= 64 || size === 256).map((size) => cell(size <= 32 ? documentSmall : documentIcon, size)).join('') : '';
    return `<div class="r" style="background:${background};color:${color}">${icons}<div class="g"></div>${documents}</div>`;
  };
  const fulls = apps.map((app) => read(app, 'logo-full.svg')).filter(Boolean).map((svg) => `<div class="f">${unique(svg.replace(/<svg([^>]*?)\swidth="[^"]*"/, '<svg$1').replace(/<svg([^>]*?)\sheight="[^"]*"/, '<svg$1').replace('<svg', '<svg width="450" height="100"'))}</div>`).join('');
  const html = `<!doctype html><html><head><style>
    body{margin:0;font:11px Segoe UI,sans-serif;background:#fff}
    .r{display:flex;align-items:flex-end;gap:18px;padding:16px 20px}
    .c{display:flex;flex-direction:column;align-items:center;gap:4px}
    .c div svg{display:block}
    .g{width:30px}
    .fs{display:flex;gap:10px;padding:10px 20px;background:#fff}
    .f svg{display:block}
  </style></head><body>
    ${apps.map((app) => row(app, '#F3F4F6', '#555') + row(app, '#1F2329', '#AAA')).join('')}
    <div class="fs">${fulls}</div>
  </body></html>`;
  fs.writeFileSync(target, await renderHtml(browser, html, 1500, apps.length * 2 * 300 + 130, false));
  console.log('Preview written to', target);
}

(async () => {
  const browser = await chromium.launch({ channel: 'msedge', headless: true });
  try {
    const previewAt = process.argv.indexOf('--preview');
    if (previewAt >= 0) {
      await preview(browser, path.resolve(process.argv[previewAt + 1]));
      return;
    }

    for (const app of apps) {
      await renderApp(browser, app);
    }
  } finally {
    await browser.close();
  }
})().catch((error) => {
  console.error(error);
  process.exit(1);
});

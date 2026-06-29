const fs = require('fs');
const path = require('path');

const srcLogoPng = path.join(__dirname, '../App/logo.png');
const srcLogoIco = path.join(__dirname, '../App/logo.ico');

const dests = [
  path.join(__dirname, '.vitepress/public'),
  path.join(__dirname, 'public')
];

// Ensure destinations exist
dests.forEach(dir => {
  if (!fs.existsSync(dir)) {
    fs.mkdirSync(dir, { recursive: true });
  }
});

// Copy assets
try {
  dests.forEach(dir => {
    fs.copyFileSync(srcLogoPng, path.join(dir, 'logo.png'));
    fs.copyFileSync(srcLogoIco, path.join(dir, 'favicon.ico'));
  });
  console.log('[copy-assets] Successfully synchronized logo.png and favicon.ico from App resources.');
} catch (err) {
  console.error('[copy-assets] Failed to synchronize assets:', err.message);
  process.exit(1);
}

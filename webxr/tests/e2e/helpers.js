import { createRequire } from 'node:module';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

// Shared browser test helpers: serve the production build with `vite preview`
// and drive it with the pre-installed Playwright Chromium (software WebGL).

const require = createRequire(import.meta.url);
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');

function loadPlaywright() {
  try {
    return require('playwright');
  } catch {
    // Fall back to a globally installed copy.
    const globalRoot = require('node:child_process').execSync('npm root -g').toString().trim();
    return require(path.join(globalRoot, 'playwright'));
  }
}

export async function startServer(port) {
  const { preview } = await import('vite');
  const server = await preview({ root, logLevel: 'silent', preview: { port, strictPort: true, open: false } });
  const url = `http://localhost:${port}/`;
  return {
    url,
    stop: () => new Promise((resolve) => server.httpServer.close(resolve)),
  };
}

export async function openGame(url) {
  const { chromium } = loadPlaywright();
  const browser = await chromium.launch({
    args: ['--use-gl=angle', '--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--autoplay-policy=no-user-gesture-required'],
  });
  const page = await browser.newPage({ viewport: { width: 1280, height: 720 } });
  const errors = [];
  page.on('pageerror', (e) => errors.push(e.message));
  page.on('console', (m) => {
    if (m.type() === 'error') errors.push(m.text());
  });
  await page.goto(url);
  await page.waitForSelector('body[data-ready=true]', { timeout: 60000 });
  return { browser, page, errors };
}

/** Poll a function in the page until it returns truthy. */
export async function waitFor(page, fn, arg, timeout = 30000) {
  await page.waitForFunction(fn, arg, { timeout, polling: 100 });
}

export const game = (page, fn, arg) => page.evaluate(fn, arg);

import { defineConfig } from 'vite';
import basicSsl from '@vitejs/plugin-basic-ssl';

// `npm run dev:quest` serves over HTTPS on your local network, because WebXR only
// works on secure pages. The Quest browser will warn about the self-signed certificate:
// choose "Advanced" → "Proceed" once.
export default defineConfig(({ mode }) => ({
  // Relative paths so the built game works from any folder or sub-path (GitHub Pages, etc.).
  base: './',
  plugins: mode === 'quest' ? [basicSsl()] : [],
  build: {
    outDir: 'dist',
    target: 'es2022',
    chunkSizeWarningLimit: 1200,
  },
  server: {
    host: true,
  },
}));

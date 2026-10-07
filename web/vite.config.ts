import { defineConfig, type Plugin } from 'vite';
import solid from 'vite-plugin-solid';
import { posterSvg } from './dev/svg.ts';

function mockServerImages(): Plugin {
  return {
    name: 'chrono-mock-server-images',
    configureServer(server) {
      server.middlewares.use((req, res, next) => {
        const match = /^\/mock\/Items\/([^/]+)\/Images\/(Primary|Backdrop)/.exec(req.url ?? '');
        if (!match) return next();
        const query = new URLSearchParams((req.url ?? '').split('?')[1] ?? '');
        const label = query.get('label') ?? match[1];
        const hue = Number(query.get('hue') ?? 220);
        const wide = match[2] === 'Backdrop';
        res.setHeader('Content-Type', 'image/svg+xml');
        res.setHeader('Cache-Control', 'max-age=3600');
        res.end(posterSvg(label, hue, wide));
      });
    }
  };
}

export default defineConfig(({ command }) => ({
  plugins: [solid(), command === 'serve' ? mockServerImages() : null],
  publicDir: false,
  build: {
    outDir: 'dist',
    emptyOutDir: true,
    target: 'es2020',
    minify: true,
    cssMinify: true,
    sourcemap: false,
    lib: {
      entry: 'src/main.ts',
      formats: ['iife'],
      name: 'JellyfinChrono',
      fileName: () => 'chrono.js',
      cssFileName: 'chrono'
    }
  },
  test: {
    include: ['test/**/*.test.ts'],
    environment: 'node'
  }
}));

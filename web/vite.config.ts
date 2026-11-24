import react from '@vitejs/plugin-react';
import { defineConfig } from 'vitest/config';

export default defineConfig({
  plugins: [react()],
  server: {
    // `npm run dev` talks to the API started by docker compose.
    proxy: { '/api': 'http://localhost:8080' },
  },
  build: {
    // The map library is most of the bundle and rarely changes: its own file stays cached across deploys.
    rollupOptions: { output: { manualChunks: { maplibre: ['maplibre-gl'] } } },
    chunkSizeWarningLimit: 900,
  },
  test: { environment: 'node' },
});

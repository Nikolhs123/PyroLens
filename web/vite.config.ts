import react from '@vitejs/plugin-react';
import { defineConfig } from 'vitest/config';

export default defineConfig({
  plugins: [react()],
  server: {
    // `npm run dev` talks to the API started by docker compose.
    proxy: { '/api': 'http://localhost:8080' },
  },
  test: { environment: 'node' },
});

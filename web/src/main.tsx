import '@fontsource/public-sans/400.css';
import '@fontsource/public-sans/500.css';
import '@fontsource/public-sans/600.css';
import '@fontsource/public-sans/700.css';
import '@fontsource/ibm-plex-mono/400.css';
import 'maplibre-gl/dist/maplibre-gl.css';
import './styles.css';
import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { App } from './App';
import { PublicApp } from './public/PublicApp';

// One web app, two pages: /public for everyone, / for dispatchers. The server sends index.html for both.
const isPublic = window.location.pathname === '/public' || window.location.pathname.startsWith('/public/');

createRoot(document.getElementById('root')!).render(<StrictMode>{isPublic ? <PublicApp /> : <App />}</StrictMode>);

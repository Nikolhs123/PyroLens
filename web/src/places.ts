/**
 * Greek towns used to name fires the way people do: "12 km southwest of Kilkis" rather than "#38".
 * Regional capitals and other well-known towns; coordinates are approximate (town centre, ±1–2 km),
 * which is plenty for orientation.
 */
const TOWNS: [name: string, lat: number, lon: number][] = [
  ['Athens', 37.984, 23.728], ['Piraeus', 37.942, 23.646], ['Marathon', 38.155, 23.963], ['Megara', 37.996, 23.344],
  ['Lavrio', 37.714, 24.056], ['Thessaloniki', 40.64, 22.944], ['Lagkadas', 40.75, 23.067], ['Patras', 38.246, 21.735],
  ['Aigio', 38.25, 22.081], ['Kalavryta', 38.033, 22.11], ['Pyrgos', 37.675, 21.441], ['Amaliada', 37.797, 21.351],
  ['Heraklion', 35.339, 25.144], ['Chania', 35.512, 24.018], ['Rethymno', 35.366, 24.482], ['Agios Nikolaos', 35.191, 25.716],
  ['Ierapetra', 35.01, 25.74], ['Sitia', 35.207, 26.105], ['Larissa', 39.639, 22.419], ['Elassona', 39.894, 22.189],
  ['Farsala', 39.296, 22.383], ['Volos', 39.362, 22.942], ['Almyros', 39.181, 22.76], ['Skiathos', 39.163, 23.49],
  ['Trikala', 39.555, 21.768], ['Kalampaka', 39.705, 21.63], ['Karditsa', 39.365, 21.922], ['Ioannina', 39.665, 20.853],
  ['Metsovo', 39.77, 21.183], ['Arta', 39.16, 20.985], ['Preveza', 38.959, 20.752], ['Igoumenitsa', 39.506, 20.266],
  ['Corfu', 39.624, 19.922], ['Lefkada', 38.833, 20.707], ['Argostoli', 38.176, 20.489], ['Zakynthos', 37.787, 20.898],
  ['Agrinio', 38.621, 21.408], ['Mesolongi', 38.371, 21.431], ['Nafpaktos', 38.392, 21.828], ['Karpenisi', 38.913, 21.796],
  ['Lamia', 38.9, 22.434], ['Domokos', 39.128, 22.3], ['Amfissa', 38.528, 22.375], ['Livadeia', 38.436, 22.875],
  ['Thebes', 38.321, 23.318], ['Chalkida', 38.463, 23.595], ['Istiaia', 38.955, 23.152], ['Karystos', 38.017, 24.42],
  ['Corinth', 37.94, 22.932], ['Nafplio', 37.568, 22.806], ['Tripoli', 37.51, 22.373], ['Sparti', 37.074, 22.43],
  ['Kalamata', 37.039, 22.114], ['Kyparissia', 37.25, 21.672], ['Kozani', 40.3, 21.789], ['Ptolemaida', 40.513, 21.678],
  ['Grevena', 40.084, 21.427], ['Kastoria', 40.521, 21.263], ['Florina', 40.782, 21.409], ['Edessa', 40.802, 22.047],
  ['Naousa', 40.629, 22.068], ['Veria', 40.523, 22.202], ['Giannitsa', 40.791, 22.408], ['Katerini', 40.27, 22.502],
  ['Kilkis', 40.993, 22.875], ['Polygyros', 40.378, 23.443], ['Nea Moudania', 40.24, 23.284], ['Serres', 41.086, 23.548],
  ['Sidirokastro', 41.236, 23.39], ['Drama', 41.15, 24.147], ['Kavala', 40.937, 24.412], ['Xanthi', 41.135, 24.888],
  ['Komotini', 41.119, 25.405], ['Alexandroupoli', 40.848, 25.874], ['Feres', 40.894, 26.172], ['Soufli', 41.199, 26.299],
  ['Didymoteicho', 41.349, 26.496], ['Orestiada', 41.503, 26.531], ['Mytilene', 39.105, 26.555], ['Myrina', 39.876, 25.062],
  ['Chios', 38.368, 26.136], ['Samos', 37.756, 26.977], ['Kos', 36.893, 27.288], ['Rhodes', 36.434, 28.217],
  ['Ermoupoli', 37.445, 24.943], ['Naxos', 37.105, 25.376], ['Thira', 36.417, 25.432],
];

const DIRECTIONS = ['north', 'northeast', 'east', 'southeast', 'south', 'southwest', 'west', 'northwest'];

/** 8-point compass word for a bearing in degrees: 0 → "north", 225 → "southwest". */
export function directionWord(degrees: number): string {
  const normalized = ((degrees % 360) + 360) % 360;
  return DIRECTIONS[Math.round(normalized / 45) % 8];
}

export interface Place {
  town: string;
  distanceKm: number;
  /** Where the fire lies as seen from the town. */
  direction: string;
}

const toRad = (deg: number) => (deg * Math.PI) / 180;

/** Great-circle distance in km (haversine). */
export function distanceKm(lat1: number, lon1: number, lat2: number, lon2: number): number {
  const dLat = toRad(lat2 - lat1);
  const dLon = toRad(lon2 - lon1);
  const a = Math.sin(dLat / 2) ** 2 + Math.cos(toRad(lat1)) * Math.cos(toRad(lat2)) * Math.sin(dLon / 2) ** 2;
  return 2 * 6371 * Math.asin(Math.sqrt(a));
}

/** Initial bearing from point 1 to point 2, degrees clockwise from north. */
export function bearing(lat1: number, lon1: number, lat2: number, lon2: number): number {
  const y = Math.sin(toRad(lon2 - lon1)) * Math.cos(toRad(lat2));
  const x = Math.cos(toRad(lat1)) * Math.sin(toRad(lat2)) - Math.sin(toRad(lat1)) * Math.cos(toRad(lat2)) * Math.cos(toRad(lon2 - lon1));
  return ((Math.atan2(y, x) * 180) / Math.PI + 360) % 360;
}

/** The closest town in the list, how far, and in which direction the fire lies from it. */
export function nearestPlace(latitude: number, longitude: number): Place {
  let best = TOWNS[0];
  let bestKm = Infinity;
  for (const town of TOWNS) {
    const km = distanceKm(town[1], town[2], latitude, longitude);
    if (km < bestKm) {
      best = town;
      bestKm = km;
    }
  }
  return { town: best[0], distanceKm: bestKm, direction: directionWord(bearing(best[1], best[2], latitude, longitude)) };
}

/** "12 km southwest of Kilkis", or "near Kilkis" within 3 km. */
export function describePlace(place: Place): string {
  if (place.distanceKm < 3) return `near ${place.town}`;
  return `${Math.round(place.distanceKm)} km ${place.direction} of ${place.town}`;
}

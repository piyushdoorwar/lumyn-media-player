// All static files live in public/ and are served under Vite's base path.
export const BASE = import.meta.env.BASE_URL; // "/lumyn-media-player/" in prod, "/" in dev

export const asset = (file) => `${BASE}assets/${file}`;

export const GITHUB_URL = "https://github.com/piyushdoorwar/lumyn-media-player";
export const SUPPORT_EMAIL = "piyushdoorwar+lumyn.mp@gmail.com";

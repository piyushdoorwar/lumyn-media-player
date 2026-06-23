import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import { resolve } from "node:path";

// GitHub Pages serves the site under /lumyn-media-player/.
// Multi-page build: one HTML entry per existing URL (/, /releases/, /policy/),
// so Pages serves real directories with no SPA fallback.
export default defineConfig({
  base: "/lumyn-media-player/",
  plugins: [react()],
  build: {
    outDir: "dist",
    rollupOptions: {
      input: {
        main: resolve(__dirname, "index.html"),
        releases: resolve(__dirname, "releases/index.html"),
        policy: resolve(__dirname, "policy/index.html"),
      },
    },
  },
});

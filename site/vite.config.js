import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import { resolve } from "node:path";

// GitHub Pages serves the site at the root of lumyn.piyushdoorwar.com.
// Multi-page build: one HTML entry per existing URL (/, /releases/, /policy/),
// so Pages serves real directories with no SPA fallback.
// 404.html is served by Pages for any missing path.
export default defineConfig({
  base: "/",
  plugins: [react()],
  build: {
    outDir: "dist",
    rollupOptions: {
      input: {
        main: resolve(__dirname, "index.html"),
        releases: resolve(__dirname, "releases/index.html"),
        policy: resolve(__dirname, "policy/index.html"),
        notfound: resolve(__dirname, "404.html"),
      },
    },
  },
});

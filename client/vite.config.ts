import react from "@vitejs/plugin-react";
import path from "path";
import { defineConfig, loadEnv } from "vite";

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, __dirname, "BLOCKS_");
  const proxyTarget = env.BLOCKS_API_BASE_URL;

  return {
    envPrefix: ["BLOCKS_"],
    plugins: [react()],
    resolve: {
      alias: {
        "@": path.resolve(__dirname, "./app"),
        "@blocks-idp": path.resolve(__dirname, "./app/idp"),
        "@blocks-lmt": path.resolve(__dirname, "./app/cross-modules/lmt"),
        "@blocks-storage": path.resolve(__dirname, "./app/cross-modules/storage"),
        "@blocks-communication": path.resolve(__dirname, "./app/cross-modules/communication"),
        "@blocks-identifier": path.resolve(__dirname, "./app/cross-modules/identifier"),
        "@blocks-localization": path.resolve(__dirname, "./app/cross-modules/localization"),
        "@blocks-utilities": path.resolve(__dirname, "./app/cross-modules/utilities"),
        "@blocks-ai": path.resolve(__dirname, "./app/cross-modules/ai"),
      },
    },
    build: {
      outDir: "../server/Api/wwwroot",
      emptyOutDir: true,
    },
    server: {
      host: true, // Listen on all addresses (0.0.0.0)
      port: 4000,
      allowedHosts: [
        "dev-cloud.seliseblocks.com",
        "localhost",
        ".seliseblocks.com",
      ],
      proxy: {
          "/dev-idp-proxy": {
            target: "https://dev-idp.blocksdevelopers.com",
            changeOrigin: true,
            secure: true,
            rewrite: (path) => path.replace(/^\/dev-idp-proxy/, ""),
          },
          ...(proxyTarget ? {
            "/api": { 
              target: proxyTarget, 
              changeOrigin: true, 
              secure: false,
            },
            "/cloudbuild": {
              target: proxyTarget,
              changeOrigin: true,
              secure: false,
            },
            "/idp": { 
              target: proxyTarget, 
              changeOrigin: true, 
              secure: false,
            },
            "/identifier": { 
              target: proxyTarget, 
              changeOrigin: true, 
              secure: false,
            },
            "/communication": { 
              target: proxyTarget, 
              changeOrigin: true, 
              secure: false,
            },
            "/cloudconfiguration": { 
              target: proxyTarget, 
              changeOrigin: true, 
              secure: false,
            },
            "/uilm": { target: proxyTarget, changeOrigin: true, secure: false },
            "/utilities": { target: proxyTarget, changeOrigin: true, secure: false },
            "/lmt": { target: proxyTarget, changeOrigin: true, secure: false },
            "/mfa": { target: proxyTarget, changeOrigin: true, secure: false },
            "/alert": { target: proxyTarget, changeOrigin: true, secure: false },
            "/blocksai-api": { target: proxyTarget, changeOrigin: true, secure: false },
            "/studio": { target: proxyTarget, changeOrigin: true, secure: false },
            "/uds": { target: proxyTarget, changeOrigin: true, secure: false },
          } : {}),
        },
    },
  };
});

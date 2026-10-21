import { fileURLToPath } from 'node:url'
import { tanstackRouter } from '@tanstack/router-plugin/vite'
import react from '@vitejs/plugin-react'
import { defineConfig, searchForWorkspaceRoot } from 'vite'

// Shared design tokens live at the repository root (design/themes.json).
const designDir = fileURLToPath(new URL('../../design', import.meta.url))

// Under Aspire the server URL is injected through service discovery; otherwise use the
// server's default launch profile.
const serverUrl =
  process.env.services__server__https__0 ??
  process.env.services__server__http__0 ??
  'http://localhost:5259'

const serverPaths = ['/api', '/auth', '/hubs', '/openapi', '/.well-known']

// https://vite.dev/config/
export default defineConfig({
  // The router plugin generates src/routeTree.gen.ts from src/routes and must run before React.
  plugins: [tanstackRouter({ target: 'react', autoCodeSplitting: true }), react()],
  resolve: {
    alias: { '@design': designDir },
  },
  server: {
    port: process.env.PORT ? Number(process.env.PORT) : 5173,
    fs: { allow: [searchForWorkspaceRoot(process.cwd()), designDir] },
    proxy: Object.fromEntries(
      serverPaths.map((path) => [path, { target: serverUrl, changeOrigin: true, ws: path === '/hubs' }]),
    ),
  },
})

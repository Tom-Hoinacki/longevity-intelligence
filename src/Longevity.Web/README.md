# Longevity Web

Standalone React/Vite frontend shell for Longevity Intelligence. Requires Node.js 20+ and npm 10+.

```powershell
Set-Location C:\Users\hoina\Documents\longevity-intelligence-web-shell\src\Longevity.Web
npm ci
npm run dev
```

Use `npm run test`, `npm run lint`, `npm run type-check`, and `npm run build`. Set `VITE_API_BASE_URL` for a future API; without it the app reports demo/offline mode and uses no credentials. Routes: `/`, `/assets`, `/claims`, `/sources`.

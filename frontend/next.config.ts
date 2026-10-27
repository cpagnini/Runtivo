import type { NextConfig } from "next";

// Il browser parla solo con Next.js: /api viene inoltrato al backend ASP.NET Core,
// così i cookie di sessione restano same-origin (niente CORS né SameSite=None).
const apiUrl = process.env.API_URL ?? "http://localhost:5044";

const nextConfig: NextConfig = {
  cacheComponents: true,
  partialPrefetching: true,
  async rewrites() {
    return [{ source: "/api/:path*", destination: `${apiUrl}/api/:path*` }];
  },
};

export default nextConfig;

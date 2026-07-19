import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  transpilePackages: ["@longevity/shared", "@longevity/supabase"],
};

export default nextConfig;

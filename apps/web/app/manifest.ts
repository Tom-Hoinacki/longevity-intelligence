import type { MetadataRoute } from "next";
import { APP_NAME, APP_TAGLINE } from "@longevity/shared";

export default function manifest(): MetadataRoute.Manifest {
  return {
    name: APP_NAME,
    short_name: "Longevity",
    description: APP_TAGLINE,
    start_url: "/",
    display: "standalone",
    background_color: "#07111f",
    theme_color: "#07111f",
    icons: [{ src: "/icon.svg", sizes: "any", type: "image/svg+xml", purpose: "any" }],
  };
}

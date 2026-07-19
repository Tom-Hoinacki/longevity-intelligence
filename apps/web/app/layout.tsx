import type { Metadata, Viewport } from "next";
import Link from "next/link";
import { APP_NAME, APP_TAGLINE } from "@longevity/shared";
import { ServiceWorkerRegistration } from "./service-worker-registration";
import "./globals.css";

export const metadata: Metadata = {
  title: APP_NAME,
  description: APP_TAGLINE,
  applicationName: APP_NAME,
};

export const viewport: Viewport = {
  themeColor: "#07111f",
  width: "device-width",
  initialScale: 1,
};

export default function RootLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return (
    <html lang="en">
      <body>
        <div className="site-frame">
          <header className="site-header">
            <Link className="brand" href="/">
              <span className="brand-mark" aria-hidden="true">LI</span>
              <span>{APP_NAME}</span>
            </Link>
            <nav aria-label="Primary navigation">
              <Link href="/assets">Assets</Link>
              <Link href="/assets">Evidence registry</Link>
            </nav>
          </header>
          <main className="main-content">{children}</main>
          <footer className="site-footer">
            <span>{APP_TAGLINE}</span>
            <span>Evidence records are not medical advice.</span>
          </footer>
        </div>
        <ServiceWorkerRegistration />
      </body>
    </html>
  );
}

import type { Metadata, Viewport } from "next";
import { Geist, Geist_Mono } from "next/font/google";
import "./globals.css";
import { SessionProvider } from "@/components/Session";
import { Header } from "@/components/Header";
import { PwaRegister } from "@/components/PwaRegister";

const geistSans = Geist({
  variable: "--font-geist-sans",
  subsets: ["latin"],
});

const geistMono = Geist_Mono({
  variable: "--font-geist-mono",
  subsets: ["latin"],
});

export const metadata: Metadata = {
  title: "Runtivo — Trova il tuo passo. Corri insieme.",
  description: "Organizza le uscite di corsa del tuo club con più gruppi di passo, senza sondaggi persi su WhatsApp.",
  icons: { apple: "/apple-touch-icon.png" },
};

export const viewport: Viewport = { themeColor: "#0f766e" };

export default function RootLayout({ children }: LayoutProps<"/">) {
  return (
    <html lang="it" className={`${geistSans.variable} ${geistMono.variable}`}>
      <body>
        <PwaRegister />
        <SessionProvider>
          <Header />
          <main className="page">{children}</main>
        </SessionProvider>
      </body>
    </html>
  );
}

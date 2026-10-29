import type { Metadata } from "next";
import { Suspense } from "react";
import { SafetyViewer } from "./SafetyViewer";

// Il token è nell'indirizzo: la pagina non va indicizzata e non deve rivelarlo ad altri siti tramite il referrer.
export const metadata: Metadata = {
  title: "Uscita condivisa — runtivo.",
  robots: { index: false, follow: false },
  referrer: "no-referrer",
};

async function SafetyLoader({ params }: { params: PageProps<"/sicurezza/[token]">["params"] }) {
  const { token } = await params;
  return <SafetyViewer token={token} />;
}

export default function SafetyPage({ params }: PageProps<"/sicurezza/[token]">) {
  return (
    <Suspense>
      <SafetyLoader params={params} />
    </Suspense>
  );
}

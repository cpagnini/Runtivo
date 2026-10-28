import type { Metadata } from "next";
import { Suspense } from "react";
import { EventView } from "./EventView";

const apiUrl = process.env.API_URL ?? "http://localhost:5044";

// Anteprima per la condivisione (WhatsApp ecc.): solo per gli eventi pubblici, letti senza sessione.
export async function generateMetadata({ params }: PageProps<"/e/[id]">): Promise<Metadata> {
  const { id } = await params;
  try {
    const res = await fetch(`${apiUrl}/api/events/${encodeURIComponent(id)}`);
    if (res.ok) {
      const ev = await res.json();
      const when = new Date(ev.startsAtUtc).toLocaleString("it-IT", {
        weekday: "long", day: "numeric", month: "long", hour: "2-digit", minute: "2-digit", timeZone: ev.timeZoneId,
      });
      const description = `${when} · ${ev.meetingPointLabel} · ${ev.groups.length} ${ev.groups.length === 1 ? "gruppo" : "gruppi"} di passo`;
      return { title: `${ev.title} — runtivo.`, description, openGraph: { title: ev.title, description, type: "website" } };
    }
  } catch {
    // Backend non raggiungibile: si usano i metadati generici.
  }
  return { title: "Uscita — runtivo." };
}

async function EventLoader({ params }: { params: PageProps<"/e/[id]">["params"] }) {
  const { id } = await params;
  return <EventView id={id} />;
}

export default function EventPage({ params }: PageProps<"/e/[id]">) {
  return (
    <Suspense>
      <EventLoader params={params} />
    </Suspense>
  );
}

"use client";

import Link from "next/link";
import { useSession } from "@/components/Session";
import styles from "./page.module.css";

const paths = [
  {
    href: "/compagni",
    title: "Cerco compagni di corsa",
    text: "Dimmi dove, quando e a che passo corri: ti propongo le persone più compatibili e organizzate l'uscita insieme.",
    cta: "Trova compagni",
  },
  {
    href: "/club",
    title: "Organizzo le uscite di un club",
    text: "Più gruppi di passo nella stessa serata, un solo link da condividere su WhatsApp. Niente più sondaggi persi.",
    cta: "Gestisci un club",
  },
];

const steps = [
  { title: "Dillo in un minuto", text: "Zona, passo, distanza e quando sei libero." },
  { title: "Scopri chi è compatibile", text: "Ti mostriamo le persone adatte, senza chat né feed." },
  { title: "Correte", text: "Vi accordate sull'uscita e vi trovate in un luogo pubblico." },
];

export default function Home() {
  const { status } = useSession();
  const signedIn = status === "authenticated";
  // Chi non ha ancora un account passa dall'accesso e poi arriva dove aveva scelto.
  const go = (href: string) => (signedIn ? href : `/accedi?next=${encodeURIComponent(href)}`);

  return (
    <div className={styles.hero}>
      <header className={styles.intro}>
        <h1 className={styles.title}>runtivo.</h1>
        <p className={styles.tagline}>Trova il tuo passo. Corri insieme.</p>
        <p className={styles.lead}>Gratis, dal telefono, senza installare nulla. Cosa vuoi fare?</p>
      </header>

      <ul className={styles.paths}>
        {paths.map((p) => (
          <li key={p.href}>
            <Link className={styles.path} href={go(p.href)}>
              <span className={styles.pathTitle}>{p.title}</span>
              <span className={styles.pathText}>{p.text}</span>
              <span className={styles.pathCta}>{p.cta} →</span>
            </Link>
          </li>
        ))}
      </ul>

      <p className="muted small">
        {signedIn ? (
          <>
            Cerchi le tue prossime uscite? <Link href="/le-mie-corse">Le mie corse</Link>
          </>
        ) : status === "anonymous" ? (
          <>
            Hai già un account? <Link href="/accedi">Accedi</Link>. Ti hanno mandato un link a un&apos;uscita? Aprilo
            direttamente.
          </>
        ) : null}
      </p>

      <section aria-labelledby="come" className="stack">
        <h2 id="come">Come funziona per chi corre da solo</h2>
        <ol className={styles.steps}>
          {steps.map((s, i) => (
            <li key={s.title}>
              <span className={styles.num} aria-hidden="true">
                {i + 1}
              </span>
              <span>
                <strong>{s.title}</strong>
                <br />
                <span className="muted">{s.text}</span>
              </span>
            </li>
          ))}
        </ol>
        <p className="muted small">
          Non mostriamo mai il tuo indirizzo né la tua posizione: scegli i luoghi da un elenco di punti pubblici.
        </p>
      </section>
    </div>
  );
}

"use client";

import Link from "next/link";
import type { ReactNode } from "react";
import { useSession } from "@/components/Session";
import styles from "./page.module.css";

const icon = (children: ReactNode) => (
  <svg viewBox="0 0 24 24" width="26" height="26" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
    {children}
  </svg>
);

const paths = [
  {
    href: "/compagni",
    icon: icon(
      <>
        <circle cx="9" cy="8" r="3.2" />
        <path d="M3 20c0-3.3 2.7-6 6-6s6 2.7 6 6" />
        <path d="M16.5 5.2a3.2 3.2 0 0 1 0 5.6M18 14.4c1.8.8 3 2.6 3 5.6" />
      </>,
    ),
    title: "Cerco compagnia per correre",
    text: "Dicci dove, quando e a che ritmo: ti presentiamo le persone più in sintonia con te e organizzate l'uscita insieme.",
    cta: "Trova compagnia",
  },
  {
    href: "/club",
    icon: icon(
      <>
        <path d="M5 21V4m0 0h11l-2 4 2 4H5" />
      </>,
    ),
    title: "Organizzo le uscite di un club",
    text: "Più gruppi di passo nella stessa serata e un solo link da condividere su WhatsApp. Niente più sondaggi persi.",
    cta: "Gestisci un club",
  },
];

const steps = [
  { title: "Raccontati in un minuto", text: "Zona, passo, distanza e quando puoi correre." },
  { title: "Scopri chi è in sintonia", text: "Persone compatibili con te, senza chat né feed infiniti." },
  { title: "Correte", text: "Vi accordate sull'uscita e vi trovate in un luogo pubblico." },
];

const promises = ["Mai il tuo indirizzo o la tua posizione", "Solo luoghi pubblici", "Puoi bloccare e segnalare, sempre"];

export default function Home() {
  const { status } = useSession();
  const signedIn = status === "authenticated";
  // Chi non ha ancora un account passa dall'accesso e poi arriva dove aveva scelto.
  const go = (href: string) => (signedIn ? href : `/accedi?next=${encodeURIComponent(href)}`);

  return (
    <div className={styles.home}>
      <section className={styles.hero}>
        <svg className={styles.route} viewBox="0 0 400 260" fill="none" aria-hidden="true">
          <path d="M-10 210C60 210 70 120 140 130s80 90 150 40S370 40 420 70" stroke="currentColor" strokeWidth="3" strokeLinecap="round" strokeDasharray="2 12" />
          <circle cx="140" cy="130" r="7" fill="currentColor" />
          <circle cx="290" cy="170" r="7" fill="currentColor" />
          <circle cx="352" cy="72" r="11" fill="var(--hot)" />
        </svg>
        <p className={styles.pill}>Gratis · dal telefono · senza installare nulla</p>
        <h1 className={styles.title}>
          Trova il tuo passo.
          <br />
          <span className={styles.accent}>Corri insieme.</span>
        </h1>
        <p className={styles.lead}>Che tu corra da sola o da solo, in coppia o con un club, qui trovi con chi farlo.</p>
      </section>

      <ul className={styles.paths}>
        {paths.map((p) => (
          <li key={p.href}>
            <Link className={styles.path} href={go(p.href)}>
              <span className={styles.pathIcon}>{p.icon}</span>
              <span className={styles.pathBody}>
                <span className={styles.pathTitle}>{p.title}</span>
                <span className={styles.pathText}>{p.text}</span>
                <span className={styles.pathCta}>{p.cta} →</span>
              </span>
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
            Hai già un account? <Link href="/accedi">Accedi</Link>. Ti hanno mandato il link a un&apos;uscita? Aprilo
            direttamente.
          </>
        ) : null}
      </p>

      <section aria-labelledby="come" className="stack">
        <h2 id="come">Come funziona se cerchi compagnia</h2>
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
      </section>

      <ul className={styles.promises} aria-label="Le nostre garanzie">
        {promises.map((t) => (
          <li key={t}>
            <span aria-hidden="true">✓</span> {t}
          </li>
        ))}
      </ul>
    </div>
  );
}

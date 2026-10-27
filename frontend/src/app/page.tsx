import Link from "next/link";
import styles from "./page.module.css";

export default function Home() {
  return (
    <div className={styles.hero}>
      <h1 className={styles.title}>runtivo.</h1>
      <p className={styles.tagline}>Trova il tuo passo. Corri insieme.</p>
      <p className={styles.lead}>
        Uscite di club con più gruppi di passo, in un solo link da condividere su WhatsApp.
      </p>
      <div className={styles.actions}>
        <Link className="button" href="/accedi">
          Accedi
        </Link>
        <Link className="button secondary" href="/club">
          I miei club
        </Link>
      </div>
    </div>
  );
}

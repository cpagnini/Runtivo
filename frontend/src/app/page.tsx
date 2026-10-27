import styles from "./page.module.css";

export default function Home() {
  return (
    <main className={styles.main}>
      <h1 className={styles.title}>runtivo.</h1>
      <p className={styles.tagline}>Trova il tuo passo. Corri insieme.</p>
      <p className={styles.lead}>
        Uscite di club con più gruppi di passo, in un solo link da condividere su WhatsApp.
      </p>
    </main>
  );
}

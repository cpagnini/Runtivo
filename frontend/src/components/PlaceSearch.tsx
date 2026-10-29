"use client";

import { useEffect, useId, useRef, useState } from "react";
import { api, errorMessage, type Place, type PlaceSuggestion } from "@/lib/api";

const MIN_CHARS = 3;
const DELAY_MS = 350;

/**
 * Punto di partenza: si scrive il nome di un parco, una piazza, un quartiere o una città e si sceglie tra i suggerimenti
 * (dati OpenStreetMap, senza indirizzi). Sotto, scorciatoie per i luoghi più usati.
 */
export function PlaceSearch({ value, shortcuts, onChange }: { value: Place | null; shortcuts: Place[]; onChange: (place: Place) => void }) {
  const listId = useId();
  const [text, setText] = useState("");
  const [results, setResults] = useState<PlaceSuggestion[]>([]);
  const [searching, setSearching] = useState(false);
  const [picking, setPicking] = useState(false);
  const [changing, setChanging] = useState(value === null);
  const [error, setError] = useState<string | null>(null);
  // Una risposta lenta non deve sovrascrivere una ricerca più recente.
  const latest = useRef(0);
  const timer = useRef<ReturnType<typeof setTimeout>>(undefined);

  useEffect(() => () => clearTimeout(timer.current), []);

  function type(next: string) {
    setText(next);
    clearTimeout(timer.current);
    const q = next.trim();
    const id = ++latest.current;
    if (q.length < MIN_CHARS) {
      setResults([]);
      setSearching(false);
      return;
    }
    setSearching(true);
    timer.current = setTimeout(async () => {
      try {
        const found = await api<PlaceSuggestion[]>(`/places/search?q=${encodeURIComponent(q)}`);
        if (id === latest.current) {
          setResults(found);
          setError(null);
        }
      } catch (e) {
        if (id === latest.current) setError(errorMessage(e));
      } finally {
        if (id === latest.current) setSearching(false);
      }
    }, DELAY_MS);
  }

  async function pick(suggestion: PlaceSuggestion) {
    setPicking(true);
    setError(null);
    try {
      onChange(await api<Place>("/places/resolve", { method: "POST", body: { ref: suggestion.ref } }));
      reset();
    } catch (e) {
      setError(errorMessage(e));
    } finally {
      setPicking(false);
    }
  }

  function reset() {
    clearTimeout(timer.current);
    latest.current++;
    setText("");
    setResults([]);
    setSearching(false);
    setChanging(false);
  }

  function choose(place: Place) {
    onChange(place);
    reset();
  }

  const q = text.trim();
  return (
    <div className="place-search">
      <span className="field-label" id={`${listId}-label`}>
        Da dove parti
      </span>

      {value && !changing ? (
        <div className="place-chosen">
          <span aria-hidden="true">📍</span>
          <span>
            <strong>{value.name}</strong>
            <span className="muted"> · {value.city}</span>
          </span>
          <button type="button" className="link-button small" onClick={() => setChanging(true)}>
            Cambia
          </button>
        </div>
      ) : (
        <>
          <input
            type="search"
            role="combobox"
            aria-labelledby={`${listId}-label`}
            aria-expanded={results.length > 0}
            aria-controls={listId}
            aria-autocomplete="list"
            autoComplete="off"
            autoCapitalize="words"
            spellCheck={false}
            placeholder="Un parco, una piazza, un quartiere, la tua città…"
            value={text}
            onChange={(e) => type(e.target.value)}
          />
          <ul id={listId} role="listbox" className="suggestions" hidden={results.length === 0}>
            {results.map((r) => (
              <li key={r.ref} role="option" aria-selected="false">
                <button type="button" disabled={picking} onClick={() => pick(r)}>
                  <strong>{r.name}</strong>
                  <span className="muted"> · {r.city}</span>
                </button>
              </li>
            ))}
          </ul>
          {searching && <p className="hint">Cerco…</p>}
          {!searching && q.length >= MIN_CHARS && results.length === 0 && !error && (
            <p className="hint">Nessun luogo trovato. Prova con il nome di un parco, di una piazza o del tuo quartiere.</p>
          )}
          {shortcuts.length > 0 && (
            <div className="chips" role="group" aria-label="Luoghi frequenti">
              {shortcuts.map((p) => (
                <button type="button" key={p.id} className="chip" onClick={() => choose(p)}>
                  {p.name}
                  <span className="muted"> · {p.city}</span>
                </button>
              ))}
            </div>
          )}
          {value && (
            <button type="button" className="link-button small" onClick={() => setChanging(false)}>
              Tieni {value.name}
            </button>
          )}
        </>
      )}
      {error && (
        <p className="error" role="alert">
          {error}
        </p>
      )}
      <span className="hint">Non chiediamo mai il tuo indirizzo né la tua posizione: usiamo solo luoghi pubblici e una zona di circa mezzo chilometro.</span>
    </div>
  );
}

"use client";

import { useState, type FormEvent } from "react";
import {
  api,
  bandLabel,
  bands,
  daysInOrder,
  errorMessage,
  formatPace,
  parsePace,
  runTypeLabel,
  slotLabel,
  weekdayLabel,
  type DayBand,
  type DayName,
  type Intent,
  type Place,
  type RunType,
} from "@/lib/api";
import { ErrorText } from "./ui";

const key = (day: DayName, band: DayBand) => `${day}|${band}`;
const kmText = (meters: number) => String(meters / 1000).replace(".", ",");

export function IntentForm({
  initial,
  places,
  onSaved,
  onCancel,
}: {
  initial: Intent | null;
  places: Place[];
  onSaved: (intent: Intent) => void;
  onCancel?: () => void;
}) {
  const [placeId, setPlaceId] = useState(initial?.homePlaceId ?? places[0]?.id ?? "");
  const [radius, setRadius] = useState(initial?.radiusKm ?? 10);
  const [paceMin, setPaceMin] = useState(initial ? formatPace(initial.paceMinSecPerKm) : "");
  const [paceMax, setPaceMax] = useState(initial ? formatPace(initial.paceMaxSecPerKm) : "");
  const [kmMin, setKmMin] = useState(initial ? kmText(initial.distanceMinMeters) : "");
  const [kmMax, setKmMax] = useState(initial ? kmText(initial.distanceMaxMeters) : "");
  const [runType, setRunType] = useState<RunType>(initial?.runType ?? "Social");
  const [slots, setSlots] = useState(() => new Set((initial?.availability ?? []).map((s) => key(s.day, s.band))));
  const [allowPairs, setAllowPairs] = useState(initial?.allowPairs ?? true);
  const [adult, setAdult] = useState(false);
  const [days, setDays] = useState(30);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const cities = [...new Set(places.map((p) => p.city))];

  const toggle = (day: DayName, band: DayBand) =>
    setSlots((prev) => {
      const next = new Set(prev);
      const k = key(day, band);
      if (next.has(k)) next.delete(k);
      else next.add(k);
      return next;
    });

  async function submit(e: FormEvent) {
    e.preventDefault();
    setError(null);

    const pMin = parsePace(paceMin);
    const pMax = parsePace(paceMax);
    if (!pMin || !pMax) return setError("Scrivi il passo da e a come minuti:secondi, ad esempio 5:30.");
    const dMin = Number(kmMin.replace(",", "."));
    const dMax = Number(kmMax.replace(",", "."));
    if (!(dMin > 0) || !(dMax > 0)) return setError("Scrivi la distanza da e a in chilometri, ad esempio 8.");
    if (slots.size === 0) return setError("Scegli almeno un giorno e una fascia oraria in cui sei libero.");

    setBusy(true);
    try {
      const saved = await api<Intent>("/me/intent", {
        method: "PUT",
        body: {
          homePlaceId: placeId,
          radiusKm: radius,
          paceMinSecPerKm: pMin,
          paceMaxSecPerKm: pMax,
          distanceMinMeters: Math.round(dMin * 1000),
          distanceMaxMeters: Math.round(dMax * 1000),
          runType,
          availability: [...slots].map((k) => {
            const [day, band] = k.split("|");
            return { day, band };
          }),
          allowPairs,
          confirmAdult: initial ? undefined : adult,
          expiresInDays: days,
        },
      });
      onSaved(saved);
    } catch (err) {
      setError(errorMessage(err));
    } finally {
      setBusy(false);
    }
  }

  return (
    <form className="stack" onSubmit={submit}>
      <label>
        Da dove parti
        <select value={placeId} onChange={(e) => setPlaceId(e.target.value)}>
          {cities.map((city) => (
            <optgroup key={city} label={city}>
              {places
                .filter((p) => p.city === city)
                .map((p) => (
                  <option key={p.id} value={p.id}>
                    {p.name}
                  </option>
                ))}
            </optgroup>
          ))}
        </select>
        <span className="hint">Scegli un luogo pubblico vicino a te: non chiediamo mai il tuo indirizzo né la tua posizione.</span>
      </label>

      <label>
        Fin dove sei disposto a spostarti: {radius} km
        <input type="range" min={5} max={30} step={1} value={radius} onChange={(e) => setRadius(Number(e.target.value))} />
      </label>

      <div className="two-cols">
        <label>
          Passo da (min/km)
          <input inputMode="numeric" required value={paceMin} onChange={(e) => setPaceMin(e.target.value)} placeholder="5:30" />
        </label>
        <label>
          Passo a (min/km)
          <input inputMode="numeric" required value={paceMax} onChange={(e) => setPaceMax(e.target.value)} placeholder="6:00" />
        </label>
        <label>
          Distanza da (km)
          <input inputMode="decimal" required value={kmMin} onChange={(e) => setKmMin(e.target.value)} placeholder="6" />
        </label>
        <label>
          Distanza a (km)
          <input inputMode="decimal" required value={kmMax} onChange={(e) => setKmMax(e.target.value)} placeholder="10" />
        </label>
      </div>

      <label>
        Che corsa cerchi
        <select value={runType} onChange={(e) => setRunType(e.target.value as RunType)}>
          {(Object.keys(runTypeLabel) as RunType[]).map((t) => (
            <option key={t} value={t}>
              {runTypeLabel[t]}
            </option>
          ))}
        </select>
      </label>

      <fieldset className="slots">
        <legend>Quando sei libero</legend>
        <div className="slot-grid" role="group">
          <span />
          {bands.map((b) => (
            <span key={b} className="small muted slot-head">
              {bandLabel[b]}
            </span>
          ))}
          {daysInOrder.map((day) => (
            <SlotRow key={day} day={day} slots={slots} toggle={toggle} />
          ))}
        </div>
      </fieldset>

      <label className="check">
        <input type="checkbox" checked={allowPairs} onChange={(e) => setAllowPairs(e.target.checked)} />
        <span>Va bene anche correre in coppia, in un luogo pubblico. Se togli la spunta propongo solo uscite di gruppo (3 o più).</span>
      </label>

      <label>
        L&apos;annuncio resta visibile per
        <select value={days} onChange={(e) => setDays(Number(e.target.value))}>
          <option value={30}>30 giorni</option>
          <option value={60}>60 giorni</option>
          <option value={90}>90 giorni</option>
        </select>
      </label>

      {!initial && (
        <label className="check">
          <input type="checkbox" required checked={adult} onChange={(e) => setAdult(e.target.checked)} />
          <span>Ho almeno 18 anni e voglio essere visibile alle persone compatibili. Mostriamo solo nome, zona generica e preferenze di corsa, mai email o posizione.</span>
        </label>
      )}

      <ErrorText>{error}</ErrorText>
      <div className="row wrap">
        <button className="button" disabled={busy || !placeId}>
          {busy ? "Salvataggio…" : initial ? "Salva l'annuncio" : "Pubblica l'annuncio"}
        </button>
        {onCancel && (
          <button type="button" className="button secondary" onClick={onCancel}>
            Annulla
          </button>
        )}
      </div>
    </form>
  );
}

function SlotRow({ day, slots, toggle }: { day: DayName; slots: Set<string>; toggle: (d: DayName, b: DayBand) => void }) {
  return (
    <>
      <span className="slot-day">{weekdayLabel[day]}</span>
      {bands.map((band) => (
        <label key={band} className="slot-cell">
          <input
            type="checkbox"
            checked={slots.has(key(day, band))}
            onChange={() => toggle(day, band)}
            aria-label={slotLabel({ day, band })}
          />
        </label>
      ))}
    </>
  );
}

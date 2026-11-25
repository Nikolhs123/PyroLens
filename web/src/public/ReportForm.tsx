import { type FormEvent, useState } from 'react';
import { type ReportKind, submitReport } from '../api';
import { describePlace, nearestPlace } from '../places';
import { REPORT_KIND } from '../reports';
import type { Spot } from './PublicMap';

interface Props {
  spot: Spot | null;
  onSpot: (spot: Spot) => void;
  onClose: () => void;
}

const MAX_NOTE = 500;
const capitalise = (text: string) => text.charAt(0).toUpperCase() + text.slice(1);

/** Three short steps: where, what, anything else. No name, no phone number. */
export function ReportForm({ spot, onSpot, onClose }: Props) {
  const [kind, setKind] = useState<ReportKind>('smoke');
  const [note, setNote] = useState('');
  const [website, setWebsite] = useState('');
  const [locating, setLocating] = useState(false);
  const [sending, setSending] = useState(false);
  const [problem, setProblem] = useState<string | null>(null);
  const [thanks, setThanks] = useState<string | null>(null);

  const locate = () => {
    if (!navigator.geolocation) {
      setProblem('This browser cannot share your location. Tap the map instead.');
      return;
    }
    setLocating(true);
    setProblem(null);
    navigator.geolocation.getCurrentPosition(
      (position) => {
        setLocating(false);
        onSpot({ latitude: position.coords.latitude, longitude: position.coords.longitude });
      },
      () => {
        setLocating(false);
        setProblem('Your location is not available. Tap the map where you see the fire instead.');
      },
      { enableHighAccuracy: true, timeout: 10_000, maximumAge: 60_000 },
    );
  };

  const send = async (e: FormEvent) => {
    e.preventDefault();
    if (!spot) return;
    setSending(true);
    setProblem(null);
    const result = await submitReport({ ...spot, kind, note: note.trim() || undefined, website: website || undefined });
    setSending(false);
    if (result.ok) {
      setThanks(result.value.message);
    } else if (result.status === 429) {
      setProblem('You have sent several reports in a short time. Please wait a few minutes and try again.');
    } else {
      setProblem(result.message);
    }
  };

  if (thanks) {
    return (
      <div className="report-done" role="status">
        <h2>Report sent</h2>
        <p>{thanks}</p>
        <button type="button" className="button primary" onClick={onClose}>
          Back to the map
        </button>
      </div>
    );
  }

  return (
    <form className="report-form" onSubmit={send} aria-labelledby="report-title">
      <h2 id="report-title">Report a fire</h2>
      <p className="hint">
        If anyone is in danger, call <strong>112</strong> first. This form only tells the dispatchers what you see.
      </p>

      <fieldset>
        <legend>1. Where is it?</legend>
        <p className="step-hint">Tap the map where you see the smoke or fire. You can drag the pin to correct it.</p>
        <div className="row">
          <button type="button" className="button" onClick={locate} disabled={locating}>
            {locating ? 'Finding you…' : 'Use my location'}
          </button>
          <span className={spot ? 'spot chosen' : 'spot'}>
            {spot ? capitalise(describePlace(nearestPlace(spot.latitude, spot.longitude))) : 'No place chosen yet'}
          </span>
        </div>
      </fieldset>

      <fieldset>
        <legend>2. What do you see?</legend>
        <div className="chips" role="radiogroup">
          {(Object.keys(REPORT_KIND) as ReportKind[]).map((k) => (
            <label key={k} className={kind === k ? 'chip on' : 'chip'}>
              <input type="radio" name="kind" value={k} checked={kind === k} onChange={() => setKind(k)} />
              {REPORT_KIND[k].label}
            </label>
          ))}
        </div>
      </fieldset>

      <fieldset>
        <legend>
          3. Anything to add? <span className="optional">(optional)</span>
        </legend>
        <textarea
          value={note}
          onChange={(e) => setNote(e.target.value.slice(0, MAX_NOTE))}
          rows={3}
          maxLength={MAX_NOTE}
          placeholder="For example: thick smoke behind the hill, moving towards the village."
          aria-describedby="note-count"
        />
        <p id="note-count" className="counter">
          {note.length} / {MAX_NOTE}
        </p>
      </fieldset>

      {/* People never see this field; bots fill it in, and their reports are quietly dropped. */}
      <label className="visually-hidden" aria-hidden="true">
        Website
        <input type="text" name="website" tabIndex={-1} autoComplete="off" value={website} onChange={(e) => setWebsite(e.target.value)} />
      </label>

      <p className="privacy">
        We do not ask for your name or phone number, and we do not keep your IP address with the report. We keep the place, what
        you saw and your note.
      </p>

      {problem && (
        <p className="message problem" role="alert">
          {problem}
        </p>
      )}

      <div className="row">
        <button type="submit" className="button primary" disabled={!spot || sending}>
          {sending ? 'Sending…' : 'Send report'}
        </button>
        <button type="button" className="button" onClick={onClose}>
          Cancel
        </button>
      </div>
    </form>
  );
}

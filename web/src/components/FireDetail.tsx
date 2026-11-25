import { useEffect, useState } from 'react';
import {
  type AirspaceReservation,
  type Forecast,
  type Incident,
  type IncidentStatus,
  type Resource,
  type TimelineEvent,
  actions,
} from '../api';
import { attentionLevel, describeAttention, sortAttention } from '../attention';
import { formatCoordinates, localStamp, relativeTime } from '../format';
import { ACTION_LABEL, STATUS, forecastSentence } from '../operations';
import { describePlace, nearestPlace } from '../places';
import { useNow } from '../usePolling';
import { AirspaceTab } from './AirspaceTab';
import { ForecastTab } from './ForecastTab';
import { Icon } from './Icon';
import { LogTab } from './LogTab';
import { UnitsTab } from './UnitsTab';

type Tab = 'forecast' | 'units' | 'airspace' | 'log';

interface Props {
  incident: Incident;
  /** undefined while loading, null when there is none yet. */
  forecast: Forecast | null | undefined;
  resources: Resource[];
  airspace: AirspaceReservation[];
  timeline: TimelineEvent[] | undefined;
  operatorKey: string | null;
  /** Something changed on the server: reload shared data. */
  onChanged: () => void;
  onBack: () => void;
}

const capitalise = (text: string) => text.charAt(0).toUpperCase() + text.slice(1);

export function FireDetail({ incident, forecast, resources, airspace, timeline, operatorKey, onChanged, onBack }: Props) {
  const [tab, setTab] = useState<Tab>('forecast');
  const [message, setMessage] = useState<{ ok: boolean; text: string } | null>(null);
  const [busy, setBusy] = useState(false);
  const now = useNow(30_000);

  // Another fire starts clean.
  useEffect(() => setMessage(null), [incident.id]);

  const onResult = (ok: boolean, text: string) => {
    setMessage({ ok, text });
    onChanged();
  };

  const move = async (to: IncidentStatus) => {
    if (!operatorKey) return;
    setBusy(true);
    const result = await actions.changeStatus(operatorKey, incident.id, incident.status, to);
    setBusy(false);
    onResult(result.ok, result.ok ? `Done: the fire is now ${STATUS[to].label.toLowerCase()}.` : result.message);
  };

  const place = nearestPlace(incident.latitude, incident.longitude);
  const reasons = sortAttention(incident.attention ?? []);
  const level = attentionLevel(incident);
  const units =resources.filter((r) => r.incidentId === incident.id).length;
  const reservations = airspace.filter((a) => a.incidentId === incident.id).length;
  const tabs: { id: Tab; label: string; count?: number }[] = [
    { id: 'forecast', label: 'Forecast' },
    { id: 'units', label: 'Units', count: units },
    { id: 'airspace', label: 'Airspace', count: reservations },
    { id: 'log', label: 'Log' },
  ];

  return (
    <article className="detail" aria-labelledby="fire-title">
      <button type="button" className="back" onClick={onBack}>
        <Icon name="back" size={16} /> All fires
      </button>

      <header className="detail-head">
        <p className="eyebrow">
          <span className="status-word" style={{ color: STATUS[incident.status].color }}>
            {STATUS[incident.status].label}
          </span>
          <span> · Fire #{incident.id}</span>
        </p>
        <h2 id="fire-title">{capitalise(describePlace(place))}</h2>
        <p className="meta">
          First seen {localStamp(incident.firstDetectedAt)}, last seen {relativeTime(incident.lastDetectedAt)}.{' '}
          {incident.detectionCount} satellite {incident.detectionCount === 1 ? 'pixel' : 'pixels'}
          {incident.maxFrpMegawatts != null && <>, strongest {incident.maxFrpMegawatts.toFixed(1)} MW</>}.
        </p>
        <p className="coords">{formatCoordinates(incident.latitude, incident.longitude)}</p>
      </header>

      {reasons.length > 0 && (
        <section className={level === 'urgent' ? 'attention' : 'attention watch'} aria-labelledby="attention-title">
          <h3 id="attention-title" className="attention-title">
            {level === 'urgent' ? 'Needs attention now' : 'Worth a look'}
          </h3>
          <ul>
            {reasons.map((a) => (
              <li key={a.rule}>{describeAttention(a, now)}</li>
            ))}
          </ul>
        </section>
      )}

      <p className="summary">{forecast === undefined ? 'Loading the forecast…' : forecastSentence(forecast)}</p>

      {operatorKey && incident.nextStatuses.length > 0 && (
        <div className="actions-row" role="group" aria-label="Change the fire's status">
          {incident.nextStatuses.map((to) => (
            <button
              key={to}
              type="button"
              className={to === 'closed' ? 'button' : 'button primary'}
              disabled={busy}
              onClick={() => move(to)}
            >
              {ACTION_LABEL[to]}
            </button>
          ))}
        </div>
      )}

      {message && (
        <p className={message.ok ? 'message ok' : 'message problem'} role={message.ok ? 'status' : 'alert'}>
          {message.text}
        </p>
      )}

      <nav className="tabs" role="tablist" aria-label="About this fire">
        {tabs.map((t) => (
          <button
            key={t.id}
            type="button"
            role="tab"
            id={`tab-${t.id}`}
            aria-selected={tab === t.id}
            aria-controls={`panel-${t.id}`}
            className={tab === t.id ? 'tab current' : 'tab'}
            onClick={() => setTab(t.id)}
          >
            {t.label}
            {t.count ? <span className="tab-count">{t.count}</span> : null}
          </button>
        ))}
      </nav>

      <div className="tab-panel" role="tabpanel" id={`panel-${tab}`} aria-labelledby={`tab-${tab}`}>
        {tab === 'forecast' && <ForecastTab incident={incident} forecast={forecast} />}
        {tab === 'units' && <UnitsTab incident={incident} resources={resources} operatorKey={operatorKey} onResult={onResult} />}
        {tab === 'airspace' && <AirspaceTab incident={incident} reservations={airspace} operatorKey={operatorKey} onResult={onResult} />}
        {tab === 'log' && <LogTab events={timeline} />}
      </div>
    </article>
  );
}

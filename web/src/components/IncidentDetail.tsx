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
import { formatCoordinates, utcStamp } from '../format';
import { ACTION_LABEL } from '../operations';
import { AirspaceTab } from './AirspaceTab';
import { ForecastTab } from './ForecastTab';
import { LogTab } from './LogTab';
import { StatusTag } from './StatusTag';
import { UnitsTab } from './UnitsTab';

type Tab = 'forecast' | 'units' | 'airspace' | 'log';
const TABS: { id: Tab; label: string }[] = [
  { id: 'forecast', label: 'FORECAST' },
  { id: 'units', label: 'UNITS' },
  { id: 'airspace', label: 'AIRSPACE' },
  { id: 'log', label: 'LOG' },
];

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
  onClose: () => void;
}

export function IncidentDetail({ incident, forecast, resources, airspace, timeline, operatorKey, onChanged, onClose }: Props) {
  const [tab, setTab] = useState<Tab>('forecast');
  const [notice, setNotice] = useState<{ ok: boolean; text: string } | null>(null);
  const [busy, setBusy] = useState(false);

  // A different incident starts clean.
  useEffect(() => setNotice(null), [incident.id]);

  const onResult = (ok: boolean, text: string) => {
    setNotice({ ok, text });
    onChanged();
  };

  const move = async (to: IncidentStatus) => {
    if (!operatorKey) return;
    setBusy(true);
    const result = await actions.changeStatus(operatorKey, incident.id, incident.status, to);
    setBusy(false);
    onResult(result.ok, result.ok ? `Incident ${incident.id} is now ${to}.` : result.message);
  };

  const committed = resources.filter((r) => r.incidentId === incident.id).length;
  const reservations = airspace.filter((a) => a.incidentId === incident.id).length;

  return (
    <section className="detail" aria-labelledby="detail-title">
      <header className="detail-head">
        <h2 id="detail-title">INC {incident.id}</h2>
        <StatusTag status={incident.status} long />
        <span className="coords">{formatCoordinates(incident.latitude, incident.longitude)}</span>
        <button type="button" className="close" onClick={onClose} title="Close (Esc)" aria-label="Close incident">
          ✕
        </button>
      </header>

      <dl className="kv kv-4">
        <div>
          <dt>PIXELS</dt>
          <dd>{incident.detectionCount}</dd>
        </div>
        <div>
          <dt>MAX FRP</dt>
          <dd>{incident.maxFrpMegawatts != null ? `${incident.maxFrpMegawatts.toFixed(1)} MW` : '–'}</dd>
        </div>
        <div>
          <dt>FIRST SEEN</dt>
          <dd>{utcStamp(incident.firstDetectedAt)}</dd>
        </div>
        <div>
          <dt>LAST SEEN</dt>
          <dd>{utcStamp(incident.lastDetectedAt)}</dd>
        </div>
      </dl>

      {operatorKey && incident.nextStatuses.length > 0 && (
        <div className="lifecycle" role="group" aria-label="Change status">
          {incident.nextStatuses.map((to) => (
            <button key={to} type="button" className={to === 'closed' ? 'btn' : 'btn btn-primary'} disabled={busy} onClick={() => move(to)}>
              {ACTION_LABEL[to].toUpperCase()}
            </button>
          ))}
        </div>
      )}

      {notice && (
        <p className={notice.ok ? 'flash ok' : 'flash bad'} role={notice.ok ? 'status' : 'alert'}>
          {notice.text}
        </p>
      )}

      <div className="tabs" role="tablist" aria-label="Incident sections">
        {TABS.map((t) => (
          <button
            key={t.id}
            type="button"
            role="tab"
            id={`tab-${t.id}`}
            aria-selected={tab === t.id}
            aria-controls={`panel-${t.id}`}
            className={tab === t.id ? 'tab active' : 'tab'}
            onClick={() => setTab(t.id)}
          >
            {t.label}
            {t.id === 'units' && committed > 0 && <span className="count">{committed}</span>}
            {t.id === 'airspace' && reservations > 0 && <span className="count">{reservations}</span>}
          </button>
        ))}
      </div>

      <div role="tabpanel" id={`panel-${tab}`} aria-labelledby={`tab-${tab}`}>
        {tab === 'forecast' && <ForecastTab incident={incident} forecast={forecast} />}
        {tab === 'units' && <UnitsTab incident={incident} resources={resources} operatorKey={operatorKey} onResult={onResult} />}
        {tab === 'airspace' && <AirspaceTab incident={incident} reservations={airspace} operatorKey={operatorKey} onResult={onResult} />}
        {tab === 'log' && <LogTab events={timeline} />}
      </div>
    </section>
  );
}

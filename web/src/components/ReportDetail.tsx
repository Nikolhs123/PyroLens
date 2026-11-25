import { useEffect, useState } from 'react';
import { type Report, actions } from '../api';
import { formatCoordinates, localStamp } from '../format';
import { REPORT_COLOR, describeReport, nearestFireText, reportPlace } from '../reports';
import { useNow } from '../usePolling';
import { Icon } from './Icon';

interface Props {
  report: Report;
  operatorKey: string | null;
  onOpenFire: (id: number) => void;
  onChanged: () => void;
  onBack: () => void;
}

/** One report from the public: where, what, the nearest fire, and (for dispatchers) the note and what to do with it. */
export function ReportDetail({ report: r, operatorKey, onOpenFire, onChanged, onBack }: Props) {
  const now = useNow(30_000);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<{ ok: boolean; text: string } | null>(null);
  useEffect(() => setMessage(null), [r.id]);

  const review = async (status: 'seen' | 'dismissed') => {
    if (!operatorKey) return;
    setBusy(true);
    const result = await actions.reviewReport(operatorKey, r.id, status);
    setBusy(false);
    setMessage({ ok: result.ok, text: result.ok ? (status === 'seen' ? 'Marked as seen.' : 'Dismissed.') : result.message });
    onChanged();
  };

  return (
    <article className="detail report-detail" aria-labelledby="report-title">
      <button type="button" className="back" onClick={onBack}>
        <Icon name="back" size={16} /> All fires
      </button>

      <header className="detail-head">
        <p className="eyebrow">
          <span className="status-word" style={{ color: REPORT_COLOR }}>
            Report from the public
          </span>
          <span> · #{r.id}</span>
          {r.status !== 'new' && (
            <span>
              {' '}
              · {r.status === 'seen' ? 'seen' : 'dismissed'} by {r.reviewedBy}
            </span>
          )}
        </p>
        <h2 id="report-title">{reportPlace(r)}</h2>
        <p className="meta">
          {describeReport(r, now)}, at {localStamp(r.submittedAt)}.
        </p>
        <p className="coords">{formatCoordinates(r.latitude, r.longitude)}</p>
      </header>

      <p className="summary">
        {nearestFireText(r)}.{' '}
        {r.nearestIncidentId != null && (
          <button type="button" className="link" onClick={() => onOpenFire(r.nearestIncidentId!)}>
            Open fire #{r.nearestIncidentId}
          </button>
        )}
        {r.nearestIncidentId == null && 'The satellites may not have seen it yet, or it is not a fire.'}
      </p>

      {r.note ? (
        <blockquote className="note">“{r.note}”</blockquote>
      ) : (
        <p className="hint">{operatorKey ? 'No note.' : 'Notes from the public are shown in operator mode only.'}</p>
      )}

      {operatorKey && r.status !== 'dismissed' && (
        <div className="actions-row" role="group" aria-label="What to do with this report">
          {r.status === 'new' && (
            <button type="button" className="button primary" disabled={busy} onClick={() => review('seen')}>
              Mark seen
            </button>
          )}
          <button type="button" className="button" disabled={busy} onClick={() => review('dismissed')}>
            Dismiss
          </button>
        </div>
      )}

      {message && (
        <p className={message.ok ? 'message ok' : 'message problem'} role={message.ok ? 'status' : 'alert'}>
          {message.text}
        </p>
      )}
    </article>
  );
}

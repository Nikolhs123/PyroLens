import type { IncidentStatus } from '../api';
import { STATUS } from '../operations';

/** Three-letter status code in its colour ("ACT"); `long` spells it out. */
export function StatusTag({ status, long }: { status: IncidentStatus; long?: boolean }) {
  const s = STATUS[status];
  return (
    <span className="st" style={{ color: s.color, borderColor: s.color }} title={s.label}>
      {long ? s.label.toUpperCase() : s.code}
    </span>
  );
}

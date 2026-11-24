import { type KeyboardEvent, useEffect, useMemo, useRef, useState } from 'react';
import type { Incident } from '../api';
import { compactAge } from '../format';
import { DEFAULT_DESCENDING, type SortKey, sortIncidents } from '../incidents';
import { RatingTag } from './RatingTag';
import { StatusTag } from './StatusTag';

interface Props {
  incidents: Incident[];
  selectedId: number | null;
  /** Toggle on click: selecting the selected row clears it. */
  onToggle: (id: number) => void;
  /** Move the selection (keyboard): always selects, or clears with null. */
  onMove: (id: number | null) => void;
}

const COLUMNS: { key: SortKey; label: string; title: string; numeric?: boolean }[] = [
  { key: 'id', label: 'ID', title: 'Incident number' },
  { key: 'status', label: 'ST', title: 'Status: DET detected, CNF confirmed, ACT active, CTN contained' },
  { key: 'rating', label: 'RTG', title: 'Fire danger rating, next 3 h' },
  { key: 'pixels', label: 'PX', title: 'Satellite pixels', numeric: true },
  { key: 'frp', label: 'FRP', title: 'Strongest pixel, fire radiative power (MW)', numeric: true },
  { key: 'seen', label: 'SEEN', title: 'Time since last detection', numeric: true },
];

export function IncidentTable({ incidents, selectedId, onToggle, onMove }: Props) {
  const [sort, setSort] = useState<{ key: SortKey; descending: boolean }>({ key: 'rating', descending: true });
  const rows = useMemo(() => sortIncidents(incidents, sort.key, sort.descending), [incidents, sort]);
  const selectedRow = useRef<HTMLTableRowElement>(null);
  const now = Date.now();

  useEffect(() => {
    selectedRow.current?.scrollIntoView({ block: 'nearest' });
  }, [selectedId]);

  const sortBy = (key: SortKey) =>
    setSort((s) => (s.key === key ? { key, descending: !s.descending } : { key, descending: DEFAULT_DESCENDING[key] }));

  const onKeyDown = (e: KeyboardEvent) => {
    if (rows.length === 0) return;
    const index = rows.findIndex((r) => r.id === selectedId);
    const go = (i: number) => {
      e.preventDefault();
      onMove(rows[Math.max(0, Math.min(rows.length - 1, i))].id);
    };
    switch (e.key) {
      case 'ArrowDown':
        return go(index < 0 ? 0 : index + 1);
      case 'ArrowUp':
        return go(index < 0 ? 0 : index - 1);
      case 'Home':
        return go(0);
      case 'End':
        return go(rows.length - 1);
      case 'Escape':
        e.preventDefault();
        return onMove(null);
    }
  };

  if (incidents.length === 0) {
    return (
      <div className="table-empty">
        <p>NO ACTIVE INCIDENTS</p>
        <p className="dim">Nothing detected in Greece in the last 48 h, or no ingestion has run yet.</p>
      </div>
    );
  }

  return (
    <div className="table-wrap" tabIndex={0} onKeyDown={onKeyDown} aria-label="Incidents. Arrow keys move the selection, Escape clears it.">
      <table className="incidents">
        <thead>
          <tr>
            {COLUMNS.map((c) => (
              <th
                key={c.key}
                scope="col"
                className={c.numeric ? 'num' : undefined}
                aria-sort={sort.key === c.key ? (sort.descending ? 'descending' : 'ascending') : 'none'}
              >
                <button type="button" title={c.title} onClick={() => sortBy(c.key)}>
                  {c.label}
                  <span className="sort" aria-hidden="true">
                    {sort.key === c.key ? (sort.descending ? '▾' : '▴') : ''}
                  </span>
                </button>
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.map((i) => {
            const selected = i.id === selectedId;
            return (
              <tr
                key={i.id}
                ref={selected ? selectedRow : undefined}
                className={selected ? 'selected' : undefined}
                aria-selected={selected}
                onClick={() => onToggle(i.id)}
              >
                <td>#{i.id}</td>
                <td>
                  <StatusTag status={i.status} />
                </td>
                <td>
                  <RatingTag incident={i} />
                </td>
                <td className="num">{i.detectionCount}</td>
                <td className="num">{i.maxFrpMegawatts != null ? i.maxFrpMegawatts.toFixed(1) : '–'}</td>
                <td className="num">{compactAge(i.lastDetectedAt, now)}</td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}

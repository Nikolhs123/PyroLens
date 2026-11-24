import type { DangerRating, Incident } from '../api';
import { RATINGS, ratingStyle } from '../ratings';

type Props = { long?: boolean } & ({ incident: Incident; rating?: never } | { rating: DangerRating; incident?: never });

/** A colour square and the band code ("VH"); `long` spells the band out. */
export function RatingTag({ incident, rating, long }: Props) {
  const style = rating ? RATINGS[rating] : ratingStyle(incident!);
  return (
    <span className="rtg" title={style.label}>
      <i style={{ background: style.color }} aria-hidden="true" />
      {long ? style.label : style.code}
    </span>
  );
}

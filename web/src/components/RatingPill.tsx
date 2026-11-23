import type { DangerRating, Incident } from '../api';
import { RATINGS, ratingStyle } from '../ratings';

type Props = { small?: boolean } & ({ incident: Incident; rating?: never } | { rating: DangerRating; incident?: never });

export function RatingPill({ incident, rating, small }: Props) {
  const style = rating ? RATINGS[rating] : ratingStyle(incident!);
  return (
    <span className={small ? 'pill pill-small' : 'pill'} style={{ background: style.color, color: style.text }}>
      {style.label}
    </span>
  );
}

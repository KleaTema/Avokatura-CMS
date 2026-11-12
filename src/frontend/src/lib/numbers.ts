/**
 * A count in Albanian notation (task 045): "." groups thousands — 1.234, 200.000 — as lib/money.ts writes amounts.
 * (Intl's sq-AL leaves four-digit numbers ungrouped, so the app groups them itself.)
 */
export function formatCount(n: number): string {
  const sign = n < 0 ? '-' : ''
  return sign + String(Math.trunc(Math.abs(n))).replace(/\B(?=(\d{3})+(?!\d))/g, '.')
}

/**
 * Security status as EVE shows it: one decimal, except that anything above 0.0 and below 0.05
 * shows as 0.1 (it is still low security, never null).
 */
export function displaySecurity(security: number): number {
  if (security > 0 && security < 0.05) return 0.1
  return Math.round(security * 10) / 10
}

/** Bucket for colouring: 0–10 for 0.0–1.0, {@link NULL_BUCKET} below 0.0. */
export function securityBucket(security: number): number {
  const shown = displaySecurity(security)
  return shown < 0 ? NULL_BUCKET : Math.round(Math.min(shown, 1) * 10)
}

export const NULL_BUCKET = 11
export const BUCKET_COUNT = 12

/** The security as text, e.g. "0.5" or "-0.3". */
export function formatSecurity(security: number): string {
  const shown = displaySecurity(security)
  return (Object.is(shown, -0) ? 0 : shown).toFixed(1)
}

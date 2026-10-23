import { useSyncExternalStore } from 'react'

const utcHour = () => new Date().getUTCHours()

function subscribe(onChange: () => void): () => void {
  // A minute is close enough to the turn of the hour.
  const timer = window.setInterval(onChange, 60_000)
  return () => window.clearInterval(timer)
}

/** The current hour of the day in UTC (0–23), which hunting profiles' hours of the day are in. */
export function useUtcHour(): number {
  return useSyncExternalStore(subscribe, utcHour)
}

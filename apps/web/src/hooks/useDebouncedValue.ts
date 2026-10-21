import { useEffect, useState } from 'react';

/** The value, once it has stopped changing for `delayMs`. For search boxes and the like. */
export function useDebouncedValue<T>(value: T, delayMs: number): T {
  const [settled, setSettled] = useState(value);

  useEffect(() => {
    const handle = window.setTimeout(() => setSettled(value), delayMs);

    return () => window.clearTimeout(handle);
  }, [value, delayMs]);

  return settled;
}

import { useCallback, useEffect, useMemo, useState } from 'react';
import { ApiError } from '../../../api/client';
import { ticketsApi } from '../../../api/tickets';
import { useDebouncedValue } from '../../../hooks/useDebouncedValue';
import type { PagedResult, TicketFilters, TicketListItem } from '../../../types/api';

const DEBOUNCE_MS = 300;

export const emptyFilters: TicketFilters = {
  search: '',
  status: undefined,
  priority: undefined,
  categoryId: undefined,
  customerId: undefined,
  assignedAgentId: undefined,
  unassignedOnly: false,
  slaStatus: undefined,
  page: 1,
  pageSize: 20,
  sortBy: 'createdAtUtc',
  sortDirection: 'desc',
};

interface Result {
  /** The request this result answers, so a newer request shows as loading. */
  requestKey: string;
  data?: PagedResult<TicketListItem>;
  error?: string;
}

/**
 * Owns the ticket list's filters and the data they produce.
 *
 * Only the search text is debounced, so typing sends one request once it settles; every other
 * filter is applied immediately. The request is derived from the whole filter object, so a new
 * filter needs no wiring here.
 */
export function useTicketList() {
  const [filters, setFilters] = useState<TicketFilters>(emptyFilters);
  const settledSearch = useDebouncedValue(filters.search, DEBOUNCE_MS);
  const [reloadCount, setReloadCount] = useState(0);
  const [result, setResult] = useState<Result>();

  // Keyed by value, not identity: a keystroke creates a new filters object, but until the
  // search settles the query it produces is the same, so no request is sent.
  const queryKey = JSON.stringify({ ...filters, search: settledSearch });
  const query = useMemo<TicketFilters>(() => JSON.parse(queryKey) as TicketFilters, [queryKey]);
  const requestKey = `${queryKey}#${reloadCount}`;

  useEffect(() => {
    // A newer request (or leaving the page) cancels this one: the browser drops the HTTP call
    // and the API's cancellation token stops the database query, so no stale result arrives.
    const controller = new AbortController();

    ticketsApi.getTickets(query, controller.signal).then(
      (data) => {
        // A response can land in the same tick as the abort; it is stale all the same.
        if (!controller.signal.aborted) {
          setResult({ requestKey, data });
        }
      },
      (error: unknown) => {
        if (controller.signal.aborted) {
          return;
        }

        const message = error instanceof ApiError ? error.message : 'Unable to load tickets.';
        setResult((current) => ({ requestKey, data: current?.data, error: message }));
      },
    );

    return () => controller.abort();
  }, [query, requestKey]);

  const updateFilters = useCallback(
    (patch: Partial<TicketFilters>) =>
      setFilters((current) => ({ ...current, ...patch, page: patch.page ?? 1 })),
    [],
  );

  const resetFilters = useCallback(() => setFilters(emptyFilters), []);

  const reload = useCallback(() => setReloadCount((count) => count + 1), []);

  return {
    filters,
    updateFilters,
    resetFilters,
    reload,
    isLoading: result?.requestKey !== requestKey,
    data: result?.data,
    error: result?.requestKey === requestKey ? result.error : undefined,
  };
}

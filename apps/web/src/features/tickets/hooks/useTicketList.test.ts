import { act, renderHook } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ticketsApi } from '../../../api/tickets';
import { page, ticketListItem } from '../../../test/fixtures';
import type { PagedResult, TicketListItem } from '../../../types/api';
import { useTicketList } from './useTicketList';

vi.mock('../../../api/tickets', () => ({
  ticketsApi: {
    getTickets: vi.fn(),
  },
}));

const getTickets = vi.mocked(ticketsApi.getTickets);

/** The filters sent with the most recent request. */
const lastQuery = () => getTickets.mock.lastCall?.[0];

/** Lets the debounce window pass, flushing whatever it triggers. */
const settle = () =>
  act(async () => {
    await vi.advanceTimersByTimeAsync(300);
  });

describe('useTicketList', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    getTickets.mockResolvedValue(page([ticketListItem()]));
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.clearAllMocks();
  });

  it('searches with the text the user typed, once the typing settles', async () => {
    const { result } = renderHook(() => useTicketList());

    await settle();

    expect(getTickets).toHaveBeenCalledTimes(1);

    act(() => {
      result.current.updateFilters({ search: 'invoice' });
    });

    // Still inside the debounce window: no second request yet.
    expect(getTickets).toHaveBeenCalledTimes(1);

    await settle();

    expect(getTickets).toHaveBeenCalledTimes(2);
    expect(lastQuery()).toMatchObject({ search: 'invoice', page: 1 });

    expect(result.current.isLoading).toBe(false);
    expect(result.current.data?.items).toHaveLength(1);
  });

  // DEFECT-117: a dropdown filter used to send no request at all.
  it('requests immediately when a non-search filter changes', async () => {
    const { result } = renderHook(() => useTicketList());

    await settle();
    getTickets.mockClear();

    await act(async () => {
      result.current.updateFilters({ status: 'InProgress' });
    });

    expect(getTickets).toHaveBeenCalledTimes(1);
    expect(lastQuery()).toMatchObject({ status: 'InProgress', search: '', page: 1 });
  });

  // DEFECT-117: clearing the search used to send the status the user had already changed.
  it('always sends the current filters together with the settled search', async () => {
    const { result } = renderHook(() => useTicketList());

    await settle();

    act(() => {
      result.current.updateFilters({ search: 'invoice' });
    });
    await settle();

    await act(async () => {
      result.current.updateFilters({ priority: 'High' });
    });

    expect(lastQuery()).toMatchObject({ search: 'invoice', priority: 'High' });

    getTickets.mockClear();

    act(() => {
      result.current.updateFilters({ search: '' });
    });
    await settle();

    expect(getTickets).toHaveBeenCalledTimes(1);
    expect(lastQuery()).toMatchObject({ search: '', priority: 'High' });
  });

  it('sends one request for a burst of keystrokes, not one per key', async () => {
    const { result } = renderHook(() => useTicketList());

    await settle();
    getTickets.mockClear();

    for (const text of ['i', 'in', 'inv', 'invo']) {
      act(() => {
        result.current.updateFilters({ search: text });
      });

      await act(async () => {
        await vi.advanceTimersByTimeAsync(100);
      });
    }

    expect(getTickets).not.toHaveBeenCalled();

    await settle();

    expect(getTickets).toHaveBeenCalledTimes(1);
    expect(lastQuery()).toMatchObject({ search: 'invo' });
  });

  it('cancels a request that a newer one overtakes, and never shows its result', async () => {
    let resolveFirst: (value: PagedResult<TicketListItem>) => void = () => {};
    getTickets.mockImplementationOnce(() => new Promise((resolve) => (resolveFirst = resolve)));

    const { result } = renderHook(() => useTicketList());

    await settle();

    const firstSignal = getTickets.mock.calls[0][1];
    getTickets.mockResolvedValueOnce(page([ticketListItem({ reference: 'TCK-0099' })]));

    await act(async () => {
      result.current.updateFilters({ status: 'Closed' });
    });

    // The superseded HTTP call is aborted, so the API stops its query too.
    expect(firstSignal?.aborted).toBe(true);

    await act(async () => {
      resolveFirst(page([ticketListItem({ reference: 'TCK-STALE' })]));
    });

    expect(result.current.data?.items[0].reference).toBe('TCK-0099');
  });
});

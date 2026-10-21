import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { ApiError } from '../../../api/client';
import { ticketsApi } from '../../../api/tickets';
import { ticketDetail } from '../../../test/fixtures';
import { EscalationPanel } from './EscalationPanel';

vi.mock('../../../api/tickets', () => ({
  ticketsApi: {
    escalate: vi.fn(),
  },
}));

const escalate = vi.mocked(ticketsApi.escalate);

describe('EscalationPanel', () => {
  afterEach(() => {
    vi.clearAllMocks();
  });

  it('blocks a reason that is too short and never calls the API', async () => {
    const onEscalated = vi.fn();
    render(<EscalationPanel ticket={ticketDetail()} onEscalated={onEscalated} />);

    await userEvent.type(screen.getByLabelText('Reason'), 'abc ');
    await userEvent.click(screen.getByRole('button', { name: 'Escalate ticket' }));

    expect(screen.getByText('Give a reason of 5 to 500 characters.')).toBeInTheDocument();
    expect(screen.getByLabelText('Reason')).toHaveAttribute('aria-invalid', 'true');
    expect(escalate).not.toHaveBeenCalled();
    expect(onEscalated).not.toHaveBeenCalled();
  });

  it('shows a 409 from the server and leaves the ticket unchanged', async () => {
    escalate.mockRejectedValue(
      new ApiError(409, 'Ticket TCK-0012 is already Critical and cannot be escalated further.'),
    );
    const onEscalated = vi.fn();
    const onConflict = vi.fn();
    render(<EscalationPanel ticket={ticketDetail()} onEscalated={onEscalated} onConflict={onConflict} />);

    await userEvent.type(screen.getByLabelText('Reason'), 'Customer has chased twice');
    await userEvent.click(screen.getByRole('button', { name: 'Escalate ticket' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('already Critical');
    // The page is asked to refetch the now-changed ticket; it is not given a changed one.
    expect(onConflict).toHaveBeenCalledOnce();
    // Only the reason is sent: who escalates comes from the signed-in agent's token.
    expect(escalate).toHaveBeenCalledWith(12, { reason: 'Customer has chased twice' });
    expect(onEscalated).not.toHaveBeenCalled();
    // The form keeps what was typed, so the user can act on the message.
    expect(screen.getByLabelText('Reason')).toHaveValue('Customer has chased twice');
  });

  it('keeps the 409 message visible once the refetched ticket can no longer be escalated', async () => {
    escalate.mockRejectedValue(
      new ApiError(409, 'Ticket TCK-0012 is already Critical and cannot be escalated further.'),
    );
    const { rerender } = render(<EscalationPanel ticket={ticketDetail()} onEscalated={vi.fn()} />);

    await userEvent.type(screen.getByLabelText('Reason'), 'Customer has chased twice');
    await userEvent.click(screen.getByRole('button', { name: 'Escalate ticket' }));
    await screen.findByRole('alert');

    // The page refetched the ticket, which is now Critical.
    rerender(<EscalationPanel ticket={ticketDetail({ priority: 'Critical' })} onEscalated={vi.fn()} />);

    expect(screen.getByRole('alert')).toHaveTextContent('cannot be escalated further');
    expect(screen.getByText(/already Critical, the highest priority/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Escalate ticket' })).not.toBeInTheDocument();
  });

  it('does not ask for a refetch on a validation error', async () => {
    escalate.mockRejectedValue(
      new ApiError(400, 'One or more validation errors occurred.', {
        errors: { Reason: ['Reason must be between 5 and 500 characters.'] },
      }),
    );
    const onConflict = vi.fn();
    render(<EscalationPanel ticket={ticketDetail()} onEscalated={vi.fn()} onConflict={onConflict} />);

    await userEvent.type(screen.getByLabelText('Reason'), 'Customer has chased twice');
    await userEvent.click(screen.getByRole('button', { name: 'Escalate ticket' }));

    expect(await screen.findByText('Reason must be between 5 and 500 characters.')).toBeInTheDocument();
    expect(onConflict).not.toHaveBeenCalled();
  });

  it('hands the escalated ticket to the page on success', async () => {
    const result = {
      ticket: ticketDetail({ priority: 'Critical' }),
      escalation: {
        id: 1,
        fromPriority: 'High' as const,
        toPriority: 'Critical' as const,
        fromAgent: null,
        toAgent: null,
        fromDueAtUtc: null,
        toDueAtUtc: null,
        reason: 'Customer has chased twice',
        escalatedBy: 'Team Lead',
        escalatedAtUtc: '2026-09-25T10:03:00Z',
      },
    };
    escalate.mockResolvedValue(result);
    const onEscalated = vi.fn();
    render(<EscalationPanel ticket={ticketDetail()} onEscalated={onEscalated} />);

    await userEvent.type(screen.getByLabelText('Reason'), 'Customer has chased twice');
    await userEvent.click(screen.getByRole('button', { name: 'Escalate ticket' }));

    expect(onEscalated).toHaveBeenCalledWith(result);
  });

  it.each([
    [{ priority: 'Critical' as const }, 'already Critical'],
    [{ status: 'Resolved' as const }, 'resolved'],
    [{ status: 'Closed' as const }, 'closed'],
  ])('offers no escalation for a ticket the server would reject (%o)', (overrides, message) => {
    render(<EscalationPanel ticket={ticketDetail(overrides)} onEscalated={vi.fn()} />);

    expect(screen.queryByRole('button', { name: 'Escalate ticket' })).not.toBeInTheDocument();
    expect(screen.getByText(new RegExp(message))).toBeInTheDocument();
  });
});

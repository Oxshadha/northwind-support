import type { EscalateTicketPayload, TicketDetail } from '../../types/api';

/** The same limits the API enforces (TicketEscalation in the domain). */
export const REASON_MIN_LENGTH = 5;
export const REASON_MAX_LENGTH = 500;

export type EscalationErrors = Partial<Record<keyof EscalateTicketPayload, string>>;

/** Checks the form as the server will, after trimming, so invalid input never reaches the API. */
export function validateEscalation(form: EscalateTicketPayload): EscalationErrors {
  const errors: EscalationErrors = {};
  const reason = form.reason.trim();

  if (reason.length < REASON_MIN_LENGTH || reason.length > REASON_MAX_LENGTH) {
    errors.reason = `Give a reason of ${REASON_MIN_LENGTH} to ${REASON_MAX_LENGTH} characters.`;
  }

  return errors;
}

/**
 * Why this ticket cannot be escalated, or undefined when it can. The API makes the same
 * check and answers 409, so this only saves a pointless round trip.
 */
export function escalationBlocker(ticket: TicketDetail): string | undefined {
  if (ticket.status === 'Resolved' || ticket.status === 'Closed') {
    return `This ticket is ${ticket.status.toLowerCase()}, so it cannot be escalated.`;
  }

  if (ticket.priority === 'Critical') {
    return 'This ticket is already Critical, the highest priority.';
  }

  return undefined;
}

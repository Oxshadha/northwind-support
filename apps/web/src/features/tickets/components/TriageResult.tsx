import { formatDateTime } from '../../../lib/format';
import type { RaisedTicket } from '../../../types/api';
import { SlaIndicator } from './SlaIndicator';
import { PriorityBadge } from './TicketBadges';

interface TriageResultProps {
  raised: RaisedTicket;
}

/** What the system decided for a new ticket, and why: priority, due date and owner. */
export function TriageResult({ raised }: TriageResultProps) {
  const { ticket, triage } = raised;

  return (
    <dl className="triage">
      <dt>Priority</dt>
      <dd>
        <PriorityBadge priority={ticket.priority} /> <span className="summary__note">{triage.priority}</span>
      </dd>

      <dt>Due</dt>
      <dd>
        {formatDateTime(ticket.dueAtUtc)}{' '}
        <SlaIndicator slaStatus={ticket.slaStatus} dueAtUtc={ticket.dueAtUtc} showDueDate={false} />
        <span className="summary__note">{triage.slaWindow}</span>
      </dd>

      <dt>Assigned to</dt>
      <dd>
        {ticket.assignedAgent?.fullName ?? <strong>Unassigned</strong>}
        <span className="summary__note">{triage.assignment}</span>
      </dd>
    </dl>
  );
}

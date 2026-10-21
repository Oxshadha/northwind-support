import { ErrorMessage } from '../../../components/ErrorMessage';
import { Spinner } from '../../../components/Spinner';
import { formatDateTime } from '../../../lib/format';
import type { AgentSummary, TicketEscalation } from '../../../types/api';

interface EscalationHistoryProps {
  escalations?: TicketEscalation[];
  isLoading: boolean;
  error?: string;
  onRetry: () => void;
}

/** Every escalation of a ticket, newest first: what changed, why, who and when. */
export function EscalationHistory({ escalations, isLoading, error, onRetry }: EscalationHistoryProps) {
  return (
    <div className="card">
      <h2>Escalation history</h2>

      {error && <ErrorMessage message={error} onRetry={onRetry} />}

      {isLoading && !escalations && <Spinner label="Loading escalation history" />}

      {escalations && escalations.length === 0 && <p className="field__hint">Not escalated yet.</p>}

      {escalations && escalations.length > 0 && (
        <ol className="history" aria-busy={isLoading}>
          {escalations.map((escalation) => (
            <li key={escalation.id} className="history__item">
              <p className="history__change">
                {escalation.fromPriority} → {escalation.toPriority}
              </p>
              <dl className="summary">
                <div>
                  <dt>Agent</dt>
                  <dd>{agentChange(escalation.fromAgent, escalation.toAgent)}</dd>
                </div>
                <div>
                  <dt>Due</dt>
                  <dd>
                    {formatDateTime(escalation.fromDueAtUtc)} → {formatDateTime(escalation.toDueAtUtc)}
                  </dd>
                </div>
              </dl>
              <p className="history__reason">{escalation.reason}</p>
              <p className="summary__note">
                By {escalation.escalatedBy}, {formatDateTime(escalation.escalatedAtUtc)}
              </p>
            </li>
          ))}
        </ol>
      )}
    </div>
  );
}

function agentChange(from: AgentSummary | null, to: AgentSummary | null): string {
  const name = (agent: AgentSummary | null) => agent?.fullName ?? 'Unassigned';

  return from?.id === to?.id ? `${name(to)} (unchanged)` : `${name(from)} → ${name(to)}`;
}

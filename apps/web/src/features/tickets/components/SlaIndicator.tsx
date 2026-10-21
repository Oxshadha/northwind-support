import { Badge } from '../../../components/Badge';
import { formatDateTime } from '../../../lib/format';
import type { SlaStatus } from '../../../types/api';
import { slaPresentation } from '../sla';

interface SlaIndicatorProps {
  slaStatus: SlaStatus;
  dueAtUtc: string | null;
  /** Show the due date beside the state, as the list does. */
  showDueDate?: boolean;
}

/** Where a ticket stands against its due date. The one SLA display, on the list and the detail page. */
export function SlaIndicator({ slaStatus, dueAtUtc, showDueDate = true }: SlaIndicatorProps) {
  const { label, tone, icon } = slaPresentation[slaStatus];

  return (
    <div className="sla-cell">
      <Badge tone={tone}>
        <span aria-hidden="true" className="badge__icon">
          {icon}
        </span>
        {label}
      </Badge>
      {showDueDate && <span className="sla-cell__due">{formatDateTime(dueAtUtc)}</span>}
    </div>
  );
}

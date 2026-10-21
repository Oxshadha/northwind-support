import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import type { TicketEscalation } from '../../../types/api';
import { EscalationHistory } from './EscalationHistory';

const escalation = (overrides: Partial<TicketEscalation> = {}): TicketEscalation => ({
  id: 1,
  fromPriority: 'Medium',
  toPriority: 'High',
  fromAgent: { id: 1, fullName: 'Alex Turner' },
  toAgent: { id: 2, fullName: 'Ben Osei' },
  fromDueAtUtc: '2026-09-24T16:12:00Z',
  toDueAtUtc: '2026-09-24T12:12:00Z',
  reason: 'Customer has chased twice',
  escalatedBy: 'Team Lead',
  escalatedAtUtc: '2026-09-24T04:12:00Z',
  ...overrides,
});

describe('EscalationHistory', () => {
  it('shows what changed, why and who', () => {
    render(<EscalationHistory escalations={[escalation()]} isLoading={false} onRetry={vi.fn()} />);

    expect(screen.getByText('Medium → High')).toBeInTheDocument();
    expect(screen.getByText('Alex Turner → Ben Osei')).toBeInTheDocument();
    expect(screen.getByText('Customer has chased twice')).toBeInTheDocument();
  });

  it('renders text people typed as text, never as markup', () => {
    const attack = '<img src=x onerror="alert(1)"><script>alert(2)</script>';
    const { container } = render(
      <EscalationHistory
        escalations={[escalation({ reason: attack, escalatedBy: '<b>Mallory</b>' })]}
        isLoading={false}
        onRetry={vi.fn()}
      />,
    );

    expect(screen.getByText(attack)).toBeInTheDocument();
    expect(screen.getByText(/<b>Mallory<\/b>/)).toBeInTheDocument();
    expect(container.querySelector('img, script, b')).toBeNull();
  });

  it('says so when a ticket has never been escalated', () => {
    render(<EscalationHistory escalations={[]} isLoading={false} onRetry={vi.fn()} />);

    expect(screen.getByText('Not escalated yet.')).toBeInTheDocument();
  });
});

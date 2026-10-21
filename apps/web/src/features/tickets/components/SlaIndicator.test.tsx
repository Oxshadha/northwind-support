import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { SlaStatus } from '../../../types/api';
import { slaPresentation } from '../sla';
import { SlaIndicator } from './SlaIndicator';

const states: SlaStatus[] = ['WithinSla', 'AtRisk', 'Breached', 'Met', 'NotApplicable'];

describe('SlaIndicator', () => {
  it.each(states)('names the %s state in words, not colour alone', (state) => {
    render(<SlaIndicator slaStatus={state} dueAtUtc="2026-09-24T16:12:00Z" />);

    expect(screen.getByText(slaPresentation[state].label)).toBeInTheDocument();
  });

  it('gives every state its own label, icon and colour', () => {
    const presented = states.map((state) => slaPresentation[state]);

    expect(new Set(presented.map((p) => p.label)).size).toBe(states.length);
    expect(new Set(presented.map((p) => p.icon)).size).toBe(states.length);
    expect(new Set(presented.map((p) => p.tone)).size).toBe(states.length);
  });

  it('hides the icon from screen readers, which read the label', () => {
    const { container } = render(<SlaIndicator slaStatus="AtRisk" dueAtUtc={null} />);

    expect(container.querySelector('.badge__icon')).toHaveAttribute('aria-hidden', 'true');
  });
});

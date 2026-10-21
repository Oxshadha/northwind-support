import type { BadgeTone } from '../../components/Badge';
import type { SlaStatus } from '../../types/api';

export interface SlaPresentation {
  label: string;
  tone: BadgeTone;
  /** A shape as well as a colour, so the state reads without colour vision too. */
  icon: string;
}

/** Every state, so adding one to the API without presenting it fails to compile. */
export const slaPresentation: Record<SlaStatus, SlaPresentation> = {
  WithinSla: { label: 'Within SLA', tone: 'info', icon: '◷' },
  AtRisk: { label: 'At risk', tone: 'warning', icon: '⚠' },
  Breached: { label: 'Breached', tone: 'danger', icon: '✕' },
  Met: { label: 'Met', tone: 'success', icon: '✓' },
  NotApplicable: { label: 'No SLA', tone: 'neutral', icon: '–' },
};

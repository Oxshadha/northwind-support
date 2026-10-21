import { useState } from 'react';
import { ApiError, toErrorMessage } from '../../../api/client';
import { ticketsApi } from '../../../api/tickets';
import { Field } from '../../../components/Field';
import type { EscalateTicketPayload, TicketDetail, TicketEscalated } from '../../../types/api';
import {
  REASON_MAX_LENGTH,
  escalationBlocker,
  validateEscalation,
  type EscalationErrors,
} from '../escalation';

interface EscalationPanelProps {
  ticket: TicketDetail;
  onEscalated: (result: TicketEscalated) => void;
  /** The server refused because the ticket changed underneath us (409): time to refetch it. */
  onConflict?: () => void;
}

const emptyForm: EscalateTicketPayload = { reason: '' };

/** Raises a ticket's priority one level, with a reason, and reports what the API decided. */
export function EscalationPanel({ ticket, onEscalated, onConflict }: EscalationPanelProps) {
  const [form, setForm] = useState<EscalateTicketPayload>(emptyForm);
  const [errors, setErrors] = useState<EscalationErrors>({});
  const [submitError, setSubmitError] = useState<string>();
  const [isSaving, setIsSaving] = useState(false);

  const blocker = escalationBlocker(ticket);

  const submit = async (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();

    const validationErrors = validateEscalation(form);
    setErrors(validationErrors);
    setSubmitError(undefined);

    if (Object.keys(validationErrors).length > 0) {
      return;
    }

    setIsSaving(true);

    try {
      // Who escalates is the signed-in agent; the API reads that from the token.
      const result = await ticketsApi.escalate(ticket.id, { reason: form.reason.trim() });
      setForm(emptyForm);
      onEscalated(result);
    } catch (caught) {
      // 400 names the field; 409 (Critical, Resolved, Closed) and anything else is shown as is.
      if (caught instanceof ApiError) {
        setErrors(fromApiErrors(caught.fieldErrors));
      }

      setSubmitError(toErrorMessage(caught, 'Could not escalate the ticket.'));

      // A 409 means our copy of the ticket is out of date (someone else escalated or closed
      // it), so the page refetches it; the message above stays to say why nothing happened.
      if (caught instanceof ApiError && caught.status === 409) {
        onConflict?.();
      }
    } finally {
      setIsSaving(false);
    }
  };

  return (
    <div className="card">
      <h2>Escalate</h2>

      {submitError && (
        <p className="field__error panel__intro" role="alert">
          {submitError}
        </p>
      )}

      {blocker ? (
        <p className="field__hint">{blocker}</p>
      ) : (
        <form className="form" onSubmit={submit} noValidate aria-busy={isSaving}>
          <p className="field__hint panel__intro">
            Raises the priority to the next level, restarts the response clock from now and
            re-checks who owns the ticket.
          </p>

          <Field id="escalation-reason" label="Reason" error={errors.reason} hint="5 to 500 characters.">
            {(fieldProps) => (
              <textarea
                {...fieldProps}
                rows={3}
                value={form.reason}
                maxLength={REASON_MAX_LENGTH}
                onChange={(event) => setForm((current) => ({ ...current, reason: event.target.value }))}
              />
            )}
          </Field>

          <button type="submit" className="button button--primary" disabled={isSaving}>
            {isSaving ? 'Escalating...' : 'Escalate ticket'}
          </button>
        </form>
      )}
    </div>
  );
}

/** The API names fields as the C# properties do ("Reason"); the form uses camelCase. */
function fromApiErrors(fieldErrors: Record<string, string[]>): EscalationErrors {
  const errors: EscalationErrors = {};

  for (const [field, messages] of Object.entries(fieldErrors)) {
    const key = field.charAt(0).toLowerCase() + field.slice(1);

    if (key === 'reason') {
      errors.reason = messages[0];
    }
  }

  return errors;
}

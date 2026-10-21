namespace SupportDesk.Application.Contracts.Tickets;

/// <summary>Why a new ticket got its priority, due date and owner, in words a team lead would use.</summary>
public sealed record TriageDto(string Priority, string SlaWindow, string Assignment);

/// <summary>Response of POST /api/tickets: the ticket as raised, and the triage decision behind it.</summary>
public sealed record RaisedTicketDto(TicketDetailDto Ticket, TriageDto Triage);

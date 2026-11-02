namespace Ash.Modules.Cases.Application.Assistants;

/// <summary>A legal assistant for the picker; <see cref="ActiveAssignments"/> = current assistant assignments on cases that are not CLOSED.</summary>
public sealed record AssignableAssistant(Guid PublicId, string FullName, int ActiveAssignments);

public sealed record AssistantRef(Guid PublicId, string Name);

/// <summary>The new row version for the next save, and the assistant that was added or removed.</summary>
public sealed record AssistantChangeResponse(uint RowVersion, AssistantRef Assistant);

namespace Ash.Modules.Notifications.Contracts;

/// <summary>
/// One notification as it is stored: a <see cref="NotificationTypes"/> code, the case it is about (internal id, for
/// the foreign key only), and texts already written out (a snapshot — case code, court, action and names only, never
/// the object or other case content). LinkPath is an app route with public ids only: /cases/{publicId}/{tab}.
/// </summary>
public sealed record NotificationMessage(string Type, long? CaseId, string Title, string? Body, string? LinkPath);

/// <summary>Who gets it: users by internal id and/or by public id (both may be given; duplicates count once).</summary>
public sealed record NotificationRecipients(IReadOnlyCollection<long> UserIds, IReadOnlyCollection<Guid> UserPublicIds)
{
    public static NotificationRecipients Of(params Guid?[] publicIds) =>
        new([], publicIds.OfType<Guid>().ToList());

    public static NotificationRecipients OfIds(IEnumerable<long> userIds) => new(userIds.ToList(), []);
}

/// <summary>
/// Stores notifications in the caller's unit of work (they commit or roll back with the action that caused them) and
/// pushes them to the recipients' open sessions only after the commit. The current user (the actor) and inactive
/// users never get one; each recipient gets one row. Used by the event handlers of task 040 and the reminders of 041.
/// </summary>
public interface INotificationSender
{
    /// <summary>Returns how many rows were stored.</summary>
    Task<int> SendAsync(NotificationMessage message, NotificationRecipients recipients, CancellationToken ct = default);
}

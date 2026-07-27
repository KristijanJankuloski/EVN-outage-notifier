using OutageNotifier.Models;

namespace OutageNotifier.Services;

public interface IEmailSender
{
    Task SendOutageNotificationAsync(IReadOnlyList<Outage> outages, CancellationToken cancellationToken);
}

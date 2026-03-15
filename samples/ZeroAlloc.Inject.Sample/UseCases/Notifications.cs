using ZeroAlloc.Inject;

namespace ZeroAlloc.Inject.Sample.UseCases;

// --- Singleton email gateway ---
public interface IEmailGateway
{
    void Send(string to, string subject, string body);
}

[Singleton]
public class ConsoleEmailGateway : IEmailGateway
{
    private int _sent;

    public void Send(string to, string subject, string body)
    {
        _sent++;
        Console.WriteLine($"  [email #{_sent}] To: {to} | Subject: {subject}");
    }
}

// --- Transient notification facade ---
public interface INotificationService
{
    void NotifyOrderPlaced(string customerEmail, decimal total);
}

[Transient]
public class NotificationService : INotificationService
{
    private readonly IEmailGateway _email;

    public NotificationService(IEmailGateway email)
    {
        _email = email;
    }

    public void NotifyOrderPlaced(string customerEmail, decimal total)
        => _email.Send(customerEmail, "Order confirmed", $"Your order of {total:C} has been placed.");
}

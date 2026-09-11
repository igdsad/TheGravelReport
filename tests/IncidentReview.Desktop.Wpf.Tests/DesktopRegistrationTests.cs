using IncidentReview.Desktop.Wpf.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace IncidentReview.Desktop.Wpf.Tests;

[TestClass]
public sealed class DesktopRegistrationTests
{
    [TestMethod]
    [TestProperty("Requirement", "QR-ARC-002")]
    public void RegistrationAddsOnlyPresentationServicesAndIsIdempotent()
    {
        var services = new ServiceCollection();

        var returned = services.AddIncidentReviewDesktop();
        _ = services.AddIncidentReviewDesktop();

        Assert.AreSame(services, returned);
        Assert.HasCount(1, services.Where(static item => item.ServiceType == typeof(IUiDispatcher)));
        Assert.HasCount(1, services.Where(
            static item => item.ServiceType == typeof(IDesktopThemeSource)));
        Assert.HasCount(1, services.Where(
            static item => item.ServiceType == typeof(IThemeController)));
        Assert.HasCount(1, services.Where(
            static item => item.ServiceType == typeof(IGlobalShortcut)));
        Assert.HasCount(1, services.Where(static item => item.ServiceType == typeof(MainWindowViewModel)));
        Assert.HasCount(1, services.Where(static item => item.ServiceType == typeof(MainWindow)));
        Assert.HasCount(1, services.Where(
            static item => item.ServiceType == typeof(IncidentReviewDesktopApplication)));
    }
}

namespace IncidentReview.Desktop.Wpf;

/// <summary>Marshals presentation state changes to the owning UI thread.</summary>
public interface IUiDispatcher
{
    /// <summary>Gets whether the caller is already on the owning UI thread.</summary>
    public bool CheckAccess();

    /// <summary>Runs an action on the owning UI thread.</summary>
    public Task InvokeAsync(Action action);
}

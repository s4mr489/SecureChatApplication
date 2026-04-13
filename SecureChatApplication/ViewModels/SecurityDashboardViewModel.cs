using SecureChatApplication.Models;
using SecureChatApplication.Services;
using System.Collections.ObjectModel;

namespace SecureChatApplication.ViewModels;

/// <summary>
/// ViewModel for the security monitoring dashboard.
/// Fetches logs, alerts, and dashboard summary from the server's /security/* endpoints
/// and exposes simulation commands for demo purposes.
/// </summary>
public sealed class SecurityDashboardViewModel : ViewModelBase
{
    private readonly ISecurityDashboardService _dashboardService;

    private bool _isLoading;
    private string _statusMessage = string.Empty;
    private string _filterText = string.Empty;
    private string _simulationResult = string.Empty;
    private DashboardSummary? _summary;

    public SecurityDashboardViewModel(ISecurityDashboardService dashboardService)
    {
        _dashboardService = dashboardService;

        RefreshCommand = new AsyncRelayCommand(RefreshAllAsync);
        SimulateBruteForceCommand = new AsyncRelayCommand(() => SimulateAttackAsync("bruteforce"));
        SimulateFloodCommand = new AsyncRelayCommand(() => SimulateAttackAsync("flood"));
        SimulateFakeKeyCommand = new AsyncRelayCommand(() => SimulateAttackAsync("fakekey"));
        ClearFilterCommand = new RelayCommand(() => FilterText = string.Empty);
    }

    // ── Collections ───────────────────────────────────────────────────────────

    public ObservableCollection<SecurityAlertEntry> Alerts { get; } = [];
    public ObservableCollection<SecurityLogEntry> Logs { get; } = [];

    // ── Properties ────────────────────────────────────────────────────────────

    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public string FilterText
    {
        get => _filterText;
        set
        {
            if (SetProperty(ref _filterText, value))
            {
                OnPropertyChanged(nameof(FilteredAlerts));
                OnPropertyChanged(nameof(FilteredLogs));
            }
        }
    }

    public string SimulationResult
    {
        get => _simulationResult;
        set => SetProperty(ref _simulationResult, value);
    }

    public DashboardSummary? Summary
    {
        get => _summary;
        private set
        {
            if (SetProperty(ref _summary, value))
            {
                OnPropertyChanged(nameof(AlertCount));
                OnPropertyChanged(nameof(ActiveUserCount));
                OnPropertyChanged(nameof(HighRiskCount));
                OnPropertyChanged(nameof(ThreatLevel));
                OnPropertyChanged(nameof(ThreatLevelColor));
            }
        }
    }

    // ── Computed stats ────────────────────────────────────────────────────────

    public int AlertCount => Summary?.AlertCount ?? 0;
    public int ActiveUserCount => Summary?.ActiveUsers?.Count ?? 0;
    public int HighRiskCount => Alerts.Count(a =>
        string.Equals(a.Severity, "High", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(a.Severity, "Critical", StringComparison.OrdinalIgnoreCase));

    public string ThreatLevel => HighRiskCount switch
    {
        0 => "Normal",
        <= 2 => "Elevated",
        <= 5 => "High",
        _ => "Critical"
    };

    public string ThreatLevelColor => ThreatLevel switch
    {
        "Normal" => "#10B981",
        "Elevated" => "#EAB308",
        "High" => "#F97316",
        "Critical" => "#EF4444",
        _ => "#8B8BA3"
    };

    // ── Filtered views ────────────────────────────────────────────────────────

    public IEnumerable<SecurityAlertEntry> FilteredAlerts =>
        string.IsNullOrWhiteSpace(FilterText)
            ? Alerts
            : Alerts.Where(a =>
                a.AlertType.Contains(FilterText, StringComparison.OrdinalIgnoreCase) ||
                a.Severity.Contains(FilterText, StringComparison.OrdinalIgnoreCase) ||
                (a.Username?.Contains(FilterText, StringComparison.OrdinalIgnoreCase) == true) ||
                a.Details.Contains(FilterText, StringComparison.OrdinalIgnoreCase));

    public IEnumerable<SecurityLogEntry> FilteredLogs =>
        string.IsNullOrWhiteSpace(FilterText)
            ? Logs
            : Logs.Where(l =>
                l.EventType.Contains(FilterText, StringComparison.OrdinalIgnoreCase) ||
                (l.Username?.Contains(FilterText, StringComparison.OrdinalIgnoreCase) == true) ||
                l.Details.Contains(FilterText, StringComparison.OrdinalIgnoreCase));

    // ── Commands ──────────────────────────────────────────────────────────────

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand SimulateBruteForceCommand { get; }
    public AsyncRelayCommand SimulateFloodCommand { get; }
    public AsyncRelayCommand SimulateFakeKeyCommand { get; }
    public RelayCommand ClearFilterCommand { get; }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Called by the view on load to populate data immediately.</summary>
    public async Task InitializeAsync() => await RefreshAllAsync();

    // ── Internals ─────────────────────────────────────────────────────────────

    private async Task RefreshAllAsync()
    {
        IsLoading = true;
        StatusMessage = "Loading security data...";

        try
        {
            var dashboardTask = _dashboardService.GetDashboardAsync();
            var logsTask = _dashboardService.GetLogsAsync();

            await Task.WhenAll(dashboardTask, logsTask);

            var dashboard = await dashboardTask;
            Summary = dashboard;

            Alerts.Clear();
            foreach (var alert in dashboard.RecentAlerts)
                Alerts.Add(alert);

            Logs.Clear();
            foreach (var log in await logsTask)
                Logs.Add(log);

            OnPropertyChanged(nameof(HighRiskCount));
            OnPropertyChanged(nameof(ThreatLevel));
            OnPropertyChanged(nameof(ThreatLevelColor));
            OnPropertyChanged(nameof(FilteredAlerts));
            OnPropertyChanged(nameof(FilteredLogs));

            StatusMessage = $"Updated {DateTime.Now:HH:mm:ss}  |  {Alerts.Count} alerts  |  {Logs.Count} events";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task SimulateAttackAsync(string attackType)
    {
        SimulationResult = $"Running '{attackType}' simulation...";

        try
        {
            var result = await _dashboardService.SimulateAttackAsync(attackType);
            SimulationResult = $"Done: {result}";

            // Refresh after a short delay so new events appear
            await Task.Delay(600);
            await RefreshAllAsync();
        }
        catch (Exception ex)
        {
            SimulationResult = $"Error: {ex.Message}";
        }
    }
}

using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using SelectiveVpnRouter.Core;
using SelectiveVpnRouter.Core.BrowserRouting;
using MessageBox = System.Windows.MessageBox;

namespace SelectiveVpnRouter.App;

public partial class MainWindow
{
    private readonly ObservableCollection<BrowserRoutingListRow> _browserRoutingRows = [];
    private BrowserRoutingControlSnapshot? _browserRoutingSnapshot;
    private bool _browserRoutingLoadInFlight;

    private void WireBrowserRoutingTab()
    {
        BrowserRulesList.ItemsSource = _browserRoutingRows;
        MainTabs.SelectionChanged += OnMainTabSelectionChanged;
    }

    private async void OnMainTabSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MainTabs.SelectedItem is not TabItem tab || tab.Header as string != "Браузер")
            return;
        await LoadBrowserRoutingAsync(showErrors: false);
    }

    private async Task LoadBrowserRoutingAsync(bool showErrors)
    {
        if (_browserRoutingLoadInFlight)
            return;

        _browserRoutingLoadInFlight = true;
        try
        {
            BrowserRoutingAppLoadResult load = await _browserRoutingSession.LoadAsync(_cts.Token);
            ApplyBrowserRoutingSnapshot(load.Snapshot, load.UserMessage, showErrors && !load.Ok);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _browserRoutingLoadInFlight = false;
        }
    }

    private void ApplyBrowserRoutingSnapshot(
        BrowserRoutingControlSnapshot? snapshot,
        string? bannerMessage,
        bool showBanner)
    {
        _browserRoutingSnapshot = snapshot?.Available == true ? snapshot : null;
        if (_lastSnapshot is not null)
            UpdateBrowserTabIntegration(_lastSnapshot);

        if (snapshot is null || !snapshot.Available)
        {
            BrowserRulesUnavailablePanel.Visibility = Visibility.Visible;
            BrowserRulesEmptyPanel.Visibility = Visibility.Collapsed;
            BrowserRulesListBorder.Visibility = Visibility.Collapsed;
            BrowserRulesUnavailableText.Text = bannerMessage ?? BrowserRoutingRulesMessages.StateUnavailable;
            _browserRoutingRows.Clear();
            return;
        }

        BrowserRulesUnavailablePanel.Visibility = Visibility.Collapsed;
        RefreshBrowserRulesFilter();
        if (showBanner && !string.IsNullOrWhiteSpace(bannerMessage))
            MessageBox.Show(bannerMessage, AppBranding.ProductName);
    }

    private void UpdateBrowserTabIntegration(ServiceSnapshot snap)
    {
        BrowserIntegrationUiPresentation.ViewModel vm =
            BrowserIntegrationUiPresentation.Map(snap.BrowserIntegration, DateTimeOffset.UtcNow);
        BrowserTabExtensionText.Text = vm.ExtensionStatus;
        BrowserTabProxyText.Text = vm.BrowserProxyStatus;
        BrowserTabSocksText.Text = vm.SocksEndpoint;
        BrowserTabVpnEgressText.Text = vm.VpnEgressStatus;
        BrowserTabRuleCountText.Text = vm.RuleCount;
    }

    private void RefreshBrowserRulesFilter()
    {
        if (_browserRoutingSnapshot is null)
            return;

        var filter = BrowserRoutingListFilter.All;
        if (BrowserFilterVpn.IsChecked == true)
            filter = BrowserRoutingListFilter.Vpn;
        else if (BrowserFilterDirect.IsChecked == true)
            filter = BrowserRoutingListFilter.Direct;
        else if (BrowserFilterDisabled.IsChecked == true)
            filter = BrowserRoutingListFilter.Disabled;

        IReadOnlyList<BrowserRoutingRulesPresentation.RuleRow> rows =
            BrowserRoutingRulesPresentation.FilterRules(
                _browserRoutingSnapshot.Rules,
                BrowserRulesSearchBox.Text,
                filter);

        _browserRoutingRows.Clear();
        foreach (BrowserRoutingRulesPresentation.RuleRow row in rows)
        {
            _browserRoutingRows.Add(new BrowserRoutingListRow
            {
                Rule = row.Rule,
                MatchTypeLabel = row.MatchTypeLabel,
                RouteLabel = row.RouteLabel,
            });
        }

        bool hasAny = _browserRoutingSnapshot.Rules.Count > 0;
        BrowserRulesEmptyPanel.Visibility = hasAny ? Visibility.Collapsed : Visibility.Visible;
        BrowserRulesListBorder.Visibility = hasAny ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnBrowserRulesFilterChanged(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton clicked && clicked.IsChecked == true)
        {
            if (clicked != BrowserFilterAll) BrowserFilterAll.IsChecked = false;
            if (clicked != BrowserFilterVpn) BrowserFilterVpn.IsChecked = false;
            if (clicked != BrowserFilterDirect) BrowserFilterDirect.IsChecked = false;
            if (clicked != BrowserFilterDisabled) BrowserFilterDisabled.IsChecked = false;
        }
        else if (BrowserFilterAll.IsChecked != true && BrowserFilterVpn.IsChecked != true &&
                 BrowserFilterDirect.IsChecked != true && BrowserFilterDisabled.IsChecked != true)
        {
            BrowserFilterAll.IsChecked = true;
        }

        RefreshBrowserRulesFilter();
    }

    private async void OnBrowserAddRule(object sender, RoutedEventArgs e) =>
        await ShowBrowserRuleEditorAsync(null);

    private async void OnBrowserEditRule(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: BrowserRoutingListRow row })
            return;
        await ShowBrowserRuleEditorAsync(row.Rule);
    }

    private async Task ShowBrowserRuleEditorAsync(BrowserRoutingRule? existing)
    {
        if (_browserRoutingSnapshot is null)
        {
            await LoadBrowserRoutingAsync(showErrors: true);
            if (_browserRoutingSnapshot is null)
                return;
        }

        var editor = new BrowserRuleEditorWindow(existing) { Owner = this };
        if (editor.ShowDialog() != true || editor.ResultRule is null)
            return;

        await RunBrowserMutationAsync(
            ct => _browserRoutingSession.UpsertAsync(editor.ResultRule, _browserRoutingSnapshot!.Revision, ct));
    }

    private async void OnBrowserRuleEnabledChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.CheckBox { Tag: BrowserRoutingListRow row, IsChecked: bool enabled })
            return;
        if (_browserRoutingSnapshot is null)
            return;

        BrowserRoutingRule updated = row.Rule with { Enabled = enabled };
        await RunBrowserMutationAsync(
            ct => _browserRoutingSession.UpsertAsync(updated, _browserRoutingSnapshot.Revision, ct));
    }

    private async void OnBrowserDeleteRule(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: BrowserRoutingListRow row })
            return;
        if (_browserRoutingSnapshot is null)
            return;

        if (MessageBox.Show(
                "Удалить правило «" + row.Name + "»?",
                AppBranding.ProductName,
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        await RunBrowserMutationAsync(
            ct => _browserRoutingSession.DeleteAsync(row.Rule.Id, _browserRoutingSnapshot!.Revision, ct));
    }

    private async void OnBrowserResetAllRules(object sender, RoutedEventArgs e)
    {
        if (_browserRoutingSnapshot is null)
            return;

        if (MessageBox.Show(
                "Удалить все правила маршрутизации сайтов? Действие нельзя отменить.",
                AppBranding.ProductName,
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        await RunBrowserMutationAsync(
            ct => _browserRoutingSession.ResetAllAsync(_browserRoutingSnapshot!.Revision, ct));
    }

    private async Task RunBrowserMutationAsync(
        Func<CancellationToken, Task<BrowserRoutingAppMutationResult>> action)
    {
        try
        {
            BrowserRoutingAppMutationResult result = await action(_cts.Token);
            if (result.Snapshot is not null)
                ApplyBrowserRoutingSnapshot(result.Snapshot, null, false);
            else if (!result.Ok)
                await LoadBrowserRoutingAsync(showErrors: false);

            if (!string.IsNullOrWhiteSpace(result.UserMessage))
                MessageBox.Show(result.UserMessage, AppBranding.ProductName);
        }
        catch (OperationCanceledException)
        {
        }
    }
}

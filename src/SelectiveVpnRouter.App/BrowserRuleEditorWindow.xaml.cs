using System.Windows;
using SelectiveVpnRouter.Core.BrowserRouting;

namespace SelectiveVpnRouter.App;

public partial class BrowserRuleEditorWindow : Window
{
    private readonly string? _existingId;

    public BrowserRoutingRule? ResultRule { get; private set; }

    public BrowserRuleEditorWindow(BrowserRoutingRule? existing)
    {
        InitializeComponent();
        _existingId = existing?.Id;
        TitleText.Text = existing is null ? "Новое правило" : "Изменить правило";

        MatchTypeBox.ItemsSource = new[]
        {
            new LabeledValue(BrowserRoutingContract.ExactHost, BrowserRoutingRulesPresentation.LabelMatchType(BrowserRoutingContract.ExactHost)),
            new LabeledValue(BrowserRoutingContract.DomainAndSubdomains,
                BrowserRoutingRulesPresentation.LabelMatchType(BrowserRoutingContract.DomainAndSubdomains)),
        };
        MatchTypeBox.DisplayMemberPath = nameof(LabeledValue.Label);
        MatchTypeBox.SelectedValuePath = nameof(LabeledValue.Value);

        RouteBox.ItemsSource = new[]
        {
            new LabeledValue(BrowserRoutingContract.RouteVpn, BrowserRoutingRulesPresentation.LabelRouteMode(BrowserRoutingContract.RouteVpn)),
            new LabeledValue(BrowserRoutingContract.RouteDirect, BrowserRoutingRulesPresentation.LabelRouteMode(BrowserRoutingContract.RouteDirect)),
        };
        RouteBox.DisplayMemberPath = nameof(LabeledValue.Label);
        RouteBox.SelectedValuePath = nameof(LabeledValue.Value);

        if (existing is null)
        {
            MatchTypeBox.SelectedValue = BrowserRoutingContract.DomainAndSubdomains;
            RouteBox.SelectedValue = BrowserRoutingContract.RouteVpn;
            EnabledBox.IsChecked = true;
        }
        else
        {
            NameBox.Text = existing.Name;
            HostBox.Text = existing.Host;
            MatchTypeBox.SelectedValue = existing.MatchType;
            RouteBox.SelectedValue = existing.RouteMode;
            EnabledBox.IsChecked = existing.Enabled;
            NotesBox.Text = existing.Notes ?? "";
        }
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        string matchType = MatchTypeBox.SelectedValue as string ?? BrowserRoutingContract.DomainAndSubdomains;
        string routeMode = RouteBox.SelectedValue as string ?? BrowserRoutingContract.RouteVpn;
        BrowserRoutingUserRuleBuilder.BuildResult built = BrowserRoutingUserRuleBuilder.TryBuild(
            _existingId,
            NameBox.Text,
            HostBox.Text,
            matchType,
            routeMode,
            EnabledBox.IsChecked == true,
            NotesBox.Text);

        if (!built.Ok || built.Rule is null)
        {
            ErrorText.Text = built.Message ?? BrowserRoutingRulesMessages.Validation;
            ErrorText.Visibility = Visibility.Visible;
            if (built.Field == "name")
                NameBox.Focus();
            else if (built.Field == "host")
                HostBox.Focus();
            return;
        }

        ResultRule = built.Rule;
        DialogResult = true;
    }

    private sealed record LabeledValue(string Value, string Label);
}

using Xunit;

namespace SelectiveVpnRouter.Core.Tests;

public sealed class SettingsSaveLayoutRegressionTests
{
    [Fact]
    public void Settings_has_exactly_one_save_settings_button()
    {
        string xaml = ReadRepoFile(Path.Combine("src", "SelectiveVpnRouter.App", "MainWindow.xaml"));
        Assert.Equal(1, CountOccurrences(xaml, "Content=\"Сохранить настройки\""));
        Assert.Equal(1, CountOccurrences(xaml, "Click=\"OnSaveSetup\""));
    }

    [Fact]
    public void Save_settings_button_is_not_inside_setup_vpn_card()
    {
        string xaml = ReadRepoFile(Path.Combine("src", "SelectiveVpnRouter.App", "MainWindow.xaml"));
        string vpnCard = ExtractNamedElementRegion(xaml, "SetupVpnCard");
        Assert.DoesNotContain("Сохранить настройки", vpnCard, StringComparison.Ordinal);
        Assert.DoesNotContain("OnSaveSetup", vpnCard, StringComparison.Ordinal);
    }

    [Fact]
    public void WizardHint_is_not_inside_work_vpn_settings_card()
    {
        string xaml = ReadRepoFile(Path.Combine("src", "SelectiveVpnRouter.App", "MainWindow.xaml"));
        string workCard = ExtractNamedElementRegion(xaml, "WorkVpnSettingsCard");
        Assert.DoesNotContain("x:Name=\"WizardHint\"", workCard, StringComparison.Ordinal);
    }

    [Fact]
    public void WizardHint_lives_in_global_save_action_row()
    {
        string xaml = ReadRepoFile(Path.Combine("src", "SelectiveVpnRouter.App", "MainWindow.xaml"));
        int settingsTab = xaml.IndexOf("x:Name=\"SettingsTab\"", StringComparison.Ordinal);
        Assert.True(settingsTab >= 0);
        string settingsRegion = xaml[settingsTab..];
        int hint = settingsRegion.IndexOf("x:Name=\"WizardHint\"", StringComparison.Ordinal);
        Assert.True(hint >= 0);
        int launchSection = settingsRegion.IndexOf("Запуск и поведение", StringComparison.Ordinal);
        int rulesSection = settingsRegion.IndexOf("Правила сети", hint, StringComparison.Ordinal);
        Assert.True(launchSection >= 0 && hint > launchSection);
        Assert.True(rulesSection > hint);
        string saveArea = settingsRegion[hint..rulesSection];
        Assert.Contains("Сохранить настройки", saveArea, StringComparison.Ordinal);
        Assert.Contains("OnSaveSetup", saveArea, StringComparison.Ordinal);
        Assert.DoesNotContain("WorkVpnSettingsCard", saveArea, StringComparison.Ordinal);
    }

    [Fact]
    public void OnSaveSetup_handler_unchanged()
    {
        string main = ReadRepoFile(Path.Combine("src", "SelectiveVpnRouter.App", "MainWindow.xaml.cs"));
        Assert.Contains("private async void OnSaveSetup(object sender, RoutedEventArgs e)", main, StringComparison.Ordinal);
        Assert.Contains("LayoutDebugPreviewStartup.PreviewSaveBlockedMessage", main, StringComparison.Ordinal);
    }

    private static int CountOccurrences(string text, string value)
    {
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private static string ExtractNamedElementRegion(string xaml, string elementName)
    {
        string marker = "x:Name=\"" + elementName + "\"";
        int start = xaml.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0);
        int borderStart = xaml.LastIndexOf("<Border", start, StringComparison.Ordinal);
        Assert.True(borderStart >= 0);
        int depth = 0;
        for (int i = borderStart; i < xaml.Length; i++)
        {
            if (xaml.AsSpan(i).StartsWith("<Border", StringComparison.Ordinal))
            {
                depth++;
            }
            else if (xaml.AsSpan(i).StartsWith("</Border>", StringComparison.Ordinal))
            {
                depth--;
                if (depth == 0)
                {
                    return xaml[borderStart..(i + "</Border>".Length)];
                }
            }
        }

        throw new InvalidOperationException("Border region not found: " + elementName);
    }

    private static string ReadRepoFile(string relativePath)
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            string candidate = Path.Combine(dir, relativePath);
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException(relativePath + " not found");
    }
}

using SelectiveVpnRouter.Core;
using SelectiveVpnRouter.Core.Portable;
using SelectiveVpnRouter.Core.Startup;
using Xunit;

namespace SelectiveVpnRouter.Core.Tests;

public sealed class StartupPreferencesTests
{
    [Fact]
    public void UiSettings_defaults_are_conservative()
    {
        var ui = new UiSettings();
        Assert.False(ui.StartWithWindows);
        Assert.False(ui.StartMinimized);
        Assert.False(ui.AutoConnectVpnOnStartup);
        Assert.True(ui.MinimizeToTray);
    }

    [Fact]
    public void CreateInitialFromLegacyServiceUi_imports_only_StartMinimized()
    {
        var legacy = new UiSettings { StartMinimized = true, MinimizeToTray = false, StartWithWindows = true };
        UiSettings initial = UiSettingsStore.CreateInitialFromLegacyServiceUi(legacy);
        Assert.True(initial.StartMinimized);
        Assert.False(initial.StartWithWindows);
        Assert.False(initial.AutoConnectVpnOnStartup);
        Assert.True(initial.MinimizeToTray);
    }

    [Fact]
    public void EnsureInitialFile_creates_once_when_ui_json_absent()
    {
        string path = Path.Combine(Path.GetTempPath(), "svr-ui-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var legacy = new UiSettings { StartMinimized = true };
            UiSettingsStore.EnsureInitialFileFromLegacyServiceUi(legacy, path);
            Assert.True(File.Exists(path));
            UiSettings loaded = UiSettingsStore.LoadOrDefault(path);
            Assert.True(loaded.StartMinimized);
            Assert.False(loaded.StartWithWindows);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void Existing_ui_json_all_false_is_not_reimported_from_legacy()
    {
        string path = Path.Combine(Path.GetTempPath(), "svr-ui-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var onDisk = new UiSettings
            {
                StartWithWindows = false,
                StartMinimized = false,
                AutoConnectVpnOnStartup = false,
            };
            UiSettingsStore.Save(onDisk, path);
            var legacy = new UiSettings { StartMinimized = true, StartWithWindows = true, AutoConnectVpnOnStartup = true };
            UiSettingsStore.EnsureInitialFileFromLegacyServiceUi(legacy, path);
            UiSettings loaded = UiSettingsStore.LoadOrDefault(path);
            Assert.False(loaded.StartWithWindows);
            Assert.False(loaded.StartMinimized);
            Assert.False(loaded.AutoConnectVpnOnStartup);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public void StartupWindowPolicy_hidden_only_when_minimized_and_bootstrap_ready(
        bool startMinimized,
        bool bootstrapRequiresUser,
        bool expectedHidden)
    {
        Assert.Equal(expectedHidden, StartupWindowPolicy.ShouldStartHidden(startMinimized, bootstrapRequiresUser));
    }

    [Fact]
    public void BootstrapRequiresUserAction_when_not_ready()
    {
        Assert.True(StartupWindowPolicy.BootstrapRequiresUserAction(PortableBootstrapState.NeedsRepair));
        Assert.False(StartupWindowPolicy.BootstrapRequiresUserAction(PortableBootstrapState.Ready));
    }

    [Theory]
    [InlineData(true, false, false, false, true)]
    [InlineData(true, true, false, false, false)]
    [InlineData(true, false, true, false, false)]
    [InlineData(true, false, false, true, false)]
    [InlineData(false, false, false, false, false)]
    public void StartupAutoConnectPolicy_one_attempt_guard(
        bool enabled,
        bool vpnReady,
        bool inProgress,
        bool alreadyAttempted,
        bool expected)
    {
        Assert.Equal(
            expected,
            StartupAutoConnectPolicy.ShouldAttemptAutoConnect(enabled, vpnReady, inProgress, alreadyAttempted));
    }

    [Fact]
    public void WindowsAutostartManager_quotes_paths_with_spaces_and_cyrillic()
    {
        const string path = @"C:\Users\Тест\VPN Route\SelectiveVpnRouter.App.exe";
        string command = WindowsAutostartManager.FormatRunCommand(path, includeAutostartMarker: true);
        Assert.Equal("\"C:\\Users\\Тест\\VPN Route\\SelectiveVpnRouter.App.exe\" --autostart", command);
    }

    [Fact]
    public void WindowsAutostartManager_enable_plan_writes_when_stale()
    {
        const string exe = @"D:\Apps\VPN Route\SelectiveVpnRouter.App.exe";
        string old = WindowsAutostartManager.FormatRunCommand(@"C:\Old\SelectiveVpnRouter.App.exe", true);
        WindowsAutostartPlan plan = WindowsAutostartManager.PlanRegistration(true, exe, old);
        Assert.True(plan.ShouldWrite);
        Assert.False(plan.ShouldDelete);
        Assert.Equal(WindowsAutostartManager.FormatRunCommand(exe, true), plan.CommandToWrite);
    }

    [Fact]
    public void WindowsAutostartManager_disable_plan_deletes_only_own_value()
    {
        WindowsAutostartPlan plan = WindowsAutostartManager.PlanRegistration(
            startWithWindowsEnabled: false,
            currentExecutablePath: @"D:\Apps\SelectiveVpnRouter.App.exe",
            registeredCommand: "\"D:\\Apps\\SelectiveVpnRouter.App.exe\" --autostart");
        Assert.False(plan.ShouldWrite);
        Assert.True(plan.ShouldDelete);
    }

    [Fact]
    public void WindowsAutostartManager_matching_registration_is_noop()
    {
        const string exe = @"D:\Apps\VPN Route\SelectiveVpnRouter.App.exe";
        string registered = WindowsAutostartManager.FormatRunCommand(exe, true);
        WindowsAutostartPlan plan = WindowsAutostartManager.PlanRegistration(true, exe, registered);
        Assert.False(plan.ShouldWrite);
        Assert.False(plan.ShouldDelete);
        Assert.True(plan.IsCurrentRegistrationMatching);
    }

    private sealed class FakeRunKey : IWindowsStartupRunKey
    {
        public string? Value { get; set; }
        public bool FailOnWrite { get; init; }
        public bool FailOnDelete { get; init; }

        public string? ReadCommand(string valueName) =>
            valueName == WindowsAutostartManager.ValueName ? Value : null;

        public void WriteCommand(string valueName, string command)
        {
            if (valueName != WindowsAutostartManager.ValueName)
            {
                return;
            }

            if (FailOnWrite)
            {
                throw new InvalidOperationException("registry write failed");
            }

            Value = command;
        }

        public void DeleteValue(string valueName)
        {
            if (valueName != WindowsAutostartManager.ValueName)
            {
                return;
            }

            if (FailOnDelete)
            {
                throw new InvalidOperationException("registry delete failed");
            }

            Value = null;
        }
    }

    [Fact]
    public void WindowsAutostartManager_apply_plan_writes_vpn_route_value_only()
    {
        var key = new FakeRunKey();
        WindowsAutostartManager.ApplyPlan(key, new WindowsAutostartPlan(true, false, "\"D:\\x.exe\" --autostart", false));
        Assert.Equal("\"D:\\x.exe\" --autostart", key.ReadCommand(WindowsAutostartManager.ValueName));
    }

    [Fact]
    public void Autostart_save_enable_success_persists_true()
    {
        string path = Path.Combine(Path.GetTempPath(), "svr-ui-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var key = new FakeRunKey();
            const string exe = @"D:\Apps\SelectiveVpnRouter.App.exe";
            var previous = new UiSettings();
            var requested = new UiSettings { StartWithWindows = true, StartMinimized = true, AutoConnectVpnOnStartup = true };
            UiSettingsSaveResult result = UiSettingsPersistence.SaveWithAutostartTransaction(
                previous,
                requested,
                key,
                exe,
                uiSettingsPath: path);
            Assert.Null(result.AutostartWarning);
            Assert.True(result.PersistedSettings.StartWithWindows);
            Assert.True(result.PersistedSettings.StartMinimized);
            Assert.True(result.PersistedSettings.AutoConnectVpnOnStartup);
            Assert.NotNull(key.Value);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void Autostart_save_enable_failure_keeps_false_and_other_prefs()
    {
        var key = new FakeRunKey { FailOnWrite = true };
        const string exe = @"D:\Apps\SelectiveVpnRouter.App.exe";
        var previous = new UiSettings();
        var requested = new UiSettings { StartWithWindows = true, StartMinimized = true, AutoConnectVpnOnStartup = true };
        string path = Path.Combine(Path.GetTempPath(), "svr-ui-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            UiSettingsSaveResult result = UiSettingsPersistence.SaveWithAutostartTransaction(
                previous,
                requested,
                key,
                exe,
                uiSettingsPath: path);
            Assert.NotNull(result.AutostartWarning);
            Assert.False(result.PersistedSettings.StartWithWindows);
            Assert.True(result.PersistedSettings.StartMinimized);
            Assert.True(result.PersistedSettings.AutoConnectVpnOnStartup);
            UiSettings reloaded = UiSettingsStore.LoadOrDefault(path);
            Assert.False(reloaded.StartWithWindows);
            Assert.True(reloaded.StartMinimized);
            Assert.True(reloaded.AutoConnectVpnOnStartup);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void Autostart_save_disable_success_persists_false()
    {
        string path = Path.Combine(Path.GetTempPath(), "svr-ui-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var key = new FakeRunKey { Value = "\"D:\\x.exe\" --autostart" };
            const string exe = @"D:\Apps\SelectiveVpnRouter.App.exe";
            var previous = new UiSettings { StartWithWindows = true };
            var requested = new UiSettings { StartWithWindows = false };
            UiSettingsSaveResult result = UiSettingsPersistence.SaveWithAutostartTransaction(
                previous,
                requested,
                key,
                exe,
                uiSettingsPath: path);
            Assert.Null(result.AutostartWarning);
            Assert.False(result.PersistedSettings.StartWithWindows);
            Assert.Null(key.Value);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void Autostart_save_disable_failure_keeps_true()
    {
        string path = Path.Combine(Path.GetTempPath(), "svr-ui-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var key = new FakeRunKey { Value = "\"D:\\x.exe\" --autostart", FailOnDelete = true };
            const string exe = @"D:\Apps\SelectiveVpnRouter.App.exe";
            var previous = new UiSettings { StartWithWindows = true };
            var requested = new UiSettings { StartWithWindows = false, StartMinimized = true, AutoConnectVpnOnStartup = true };
            UiSettingsSaveResult result = UiSettingsPersistence.SaveWithAutostartTransaction(
                previous,
                requested,
                key,
                exe,
                uiSettingsPath: path);
            Assert.NotNull(result.AutostartWarning);
            Assert.True(result.PersistedSettings.StartWithWindows);
            Assert.True(result.PersistedSettings.StartMinimized);
            Assert.True(result.PersistedSettings.AutoConnectVpnOnStartup);
            Assert.NotNull(key.Value);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void Startup_reconcile_repairs_stale_command_when_enabled()
    {
        var key = new FakeRunKey { Value = "\"C:\\Old\\SelectiveVpnRouter.App.exe\" --autostart" };
        const string exe = @"D:\New\SelectiveVpnRouter.App.exe";
        var ui = new UiSettings { StartWithWindows = true };
        string? error = UiSettingsPersistence.TryReconcileAutostartRegistration(ui, key, exe);
        Assert.Null(error);
        Assert.Equal(WindowsAutostartManager.FormatRunCommand(exe, true), key.Value);
    }

    [Fact]
    public void Startup_reconcile_no_write_when_exact()
    {
        const string exe = @"D:\Apps\SelectiveVpnRouter.App.exe";
        string command = WindowsAutostartManager.FormatRunCommand(exe, true);
        var key = new FakeRunKey { Value = command };
        var ui = new UiSettings { StartWithWindows = true };
        string? error = UiSettingsPersistence.TryReconcileAutostartRegistration(ui, key, exe);
        Assert.Null(error);
        Assert.Equal(command, key.Value);
    }

    [Fact]
    public void Startup_reconcile_off_when_registry_absent_is_noop()
    {
        var key = new FakeRunKey();
        const string exe = @"D:\Apps\SelectiveVpnRouter.App.exe";
        var ui = new UiSettings { StartWithWindows = false };
        string? error = UiSettingsPersistence.TryReconcileAutostartRegistration(ui, key, exe);
        Assert.Null(error);
        Assert.Null(key.Value);
    }

    [Fact]
    public void Startup_reconcile_off_removes_own_vpn_route_value()
    {
        var key = new FakeRunKey { Value = "\"D:\\Apps\\SelectiveVpnRouter.App.exe\" --autostart" };
        const string exe = @"D:\Apps\SelectiveVpnRouter.App.exe";
        var ui = new UiSettings { StartWithWindows = false };
        string? error = UiSettingsPersistence.TryReconcileAutostartRegistration(ui, key, exe);
        Assert.Null(error);
        Assert.Null(key.Value);
    }

    [Fact]
    public void Startup_reconcile_off_does_not_touch_unrelated_run_values()
    {
        var key = new MultiValueFakeRunKey();
        key.Set("Other App", "C:\\Other.exe");
        key.Set(WindowsAutostartManager.ValueName, "\"D:\\Apps\\SelectiveVpnRouter.App.exe\" --autostart");
        const string exe = @"D:\Apps\SelectiveVpnRouter.App.exe";
        var ui = new UiSettings { StartWithWindows = false };
        string? error = UiSettingsPersistence.TryReconcileAutostartRegistration(ui, key, exe);
        Assert.Null(error);
        Assert.Null(key.Get(WindowsAutostartManager.ValueName));
        Assert.Equal("C:\\Other.exe", key.Get("Other App"));
    }

    private sealed class MultiValueFakeRunKey : IWindowsStartupRunKey
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);

        public string? Get(string valueName) =>
            _values.TryGetValue(valueName, out string? value) ? value : null;

        public void Set(string valueName, string command) => _values[valueName] = command;

        public string? ReadCommand(string valueName) => Get(valueName);

        public void WriteCommand(string valueName, string command) => Set(valueName, command);

        public void DeleteValue(string valueName) => _values.Remove(valueName);
    }
}

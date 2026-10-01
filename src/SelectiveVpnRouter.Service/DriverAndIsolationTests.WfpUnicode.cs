using SelectiveVpnRouter.Core;
using SelectiveVpnRouter.Network;

namespace SelectiveVpnRouter.Service;

internal static partial class DriverAndIsolationTests
{
    private const string UnicodeProbeSegment = "\u0422\u0435\u0441\u0442 VPN Route";

    private static string WfpUnicodeProbeDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "VPN Route WFP Tests",
            UnicodeProbeSegment);

    private static async Task<DiagnosticResult> WfpProbeUnicodeRedirect(RouterEngine engine, CancellationToken ct)
    {
        if (FindProbe() is null)
        {
            return Fail("wfp-probe-unicode-redirect", Bilingual("Probe.exe missing.", "Probe.exe ne naiden."));
        }

        if (!engine.Snapshot().DriverLoaded)
        {
            return Fail("wfp-probe-unicode-redirect", Bilingual(
                "Callout driver not loaded.",
                "Callout-draiver ne zagruzhen."));
        }

        if (!engine.Snapshot().Vpn.Connected || engine.ProxyPort is null)
        {
            return Fail("wfp-probe-unicode-redirect", Bilingual(
                "VPN not connected or proxy not running.",
                "VPN ne podklyuchen ili proksi ne zapushchen."));
        }

        string url = engine.Config.Vpn.PublicIpEndpoint ?? "https://api.ipify.org";
        CalloutArmStatus calloutBefore = engine.Snapshot().Callout;
        TransparentProxyDiagnostics proxyBefore = engine.Snapshot().ProxyDiagnostics;

        string asciiDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SVRProbeAscii");
        string asciiExe;
        string unicodeExe;
        try
        {
            asciiExe = Path.GetFullPath(ProbeCopyHelper.PrepareProbeCopy(asciiDir));
            unicodeExe = Path.GetFullPath(ProbeCopyHelper.PrepareProbeCopy(WfpUnicodeProbeDirectory()));
        }
        catch (Exception ex)
        {
            return Fail("wfp-probe-unicode-redirect", ProbeInfrastructureFailure(ex.Message));
        }

        (DiagnosticResult? sanityFail, string? baselineDirectPublicIp) = await EnsureProbeHttpSanityAsync(
            "wfp-probe-unicode-redirect",
            unicodeExe,
            url,
            ct).ConfigureAwait(false);
        if (sanityFail is not null)
        {
            return sanityFail;
        }

        try
        {
            ProbeRedirectCaseResult ascii = await RunProbeRedirectCase(
                engine, asciiExe, WfpAppIdentityPathMode.Default, asciiExe, url, calloutBefore, proxyBefore, ct).ConfigureAwait(false);
            ProbeRedirectCaseResult unicode = await RunProbeRedirectCase(
                engine, unicodeExe, WfpAppIdentityPathMode.Default, unicodeExe, url, engine.Snapshot().Callout, engine.Snapshot().ProxyDiagnostics, ct).ConfigureAwait(false);

            bool asciiPass = ProbeRedirectPass(ascii, baselineDirectPublicIp);
            bool unicodePass = ProbeRedirectPass(unicode, baselineDirectPublicIp);
            string detail = FormatProbeRedirectCase("ASCII", ascii) + " | " + FormatProbeRedirectCase("UNICODE", unicode);
            if (asciiPass && unicodePass)
            {
                return Pass("wfp-probe-unicode-redirect", Bilingual(
                    "WFP probe redirect PASS (ASCII + Unicode, normalized ALE_APP_ID). " + detail,
                    "WFP probe redirect PASS (ASCII + Unicode). " + detail));
            }

            return Fail("wfp-probe-unicode-redirect", Bilingual(
                "WFP probe redirect FAIL. " + detail,
                "WFP probe redirect FAIL. " + detail));
        }
        finally
        {
            await engine.RemoveTempAppVpnRouteAsync().ConfigureAwait(false);
        }
    }

    private static async Task<DiagnosticResult> WfpProbeShortAppIdAb(RouterEngine engine, CancellationToken ct)
    {
        if (FindProbe() is null)
        {
            return Fail("wfp-probe-short-appid-ab", Bilingual("Probe.exe missing.", "Probe.exe ne naiden."));
        }

        if (!engine.Snapshot().DriverLoaded || !engine.Snapshot().Vpn.Connected || engine.ProxyPort is null)
        {
            return Fail("wfp-probe-short-appid-ab", Bilingual(
                "Requires loaded driver, connected VPN, and proxy.",
                "Nuzhny draiver, VPN i proksi."));
        }

        string unicodeDir = WfpUnicodeProbeDirectory();
        string longExe;
        try
        {
            longExe = Path.GetFullPath(ProbeCopyHelper.PrepareProbeCopy(unicodeDir));
        }
        catch (Exception ex)
        {
            return Fail("wfp-probe-short-appid-ab", ProbeInfrastructureFailure(ex.Message));
        }

        if (!WfpAppIdentity.ContainsNonAscii(longExe))
        {
            return Fail("wfp-probe-short-appid-ab", Bilingual(
                "Unicode probe directory missing non-ASCII segment: " + longExe,
                "Net non-ASCII segmenta v puti: " + longExe));
        }

        if (!WfpAppIdentity.TryGetShortPath(longExe, out string shortExe) || !File.Exists(shortExe))
        {
            return Fail("wfp-probe-short-appid-ab", Bilingual(
                "8.3 short path unavailable for Unicode probe.",
                "8.3 put nedostupen."));
        }

        string url = engine.Config.Vpn.PublicIpEndpoint ?? "https://api.ipify.org";
        await engine.RemoveTempAppVpnRouteAsync().ConfigureAwait(false);

        (DiagnosticResult? sanityFail, string? baselineDirectPublicIp) = await EnsureProbeHttpSanityAsync(
            "wfp-probe-short-appid-ab",
            longExe,
            url,
            ct).ConfigureAwait(false);
        if (sanityFail is not null)
        {
            return sanityFail;
        }

        try
        {
            CalloutArmStatus b0 = engine.Snapshot().Callout;
            TransparentProxyDiagnostics p0 = engine.Snapshot().ProxyDiagnostics;
            ProbeRedirectCaseResult caseA = await RunProbeRedirectCase(
                engine, shortExe, WfpAppIdentityPathMode.ShortPathOnly, longExe, url, b0, p0, ct, longExe, shortExe).ConfigureAwait(false);
            if (caseA.InfrastructureFailure is not null)
            {
                return Fail(
                    "wfp-probe-short-appid-ab",
                    ProbeInfrastructureFailure(caseA.InfrastructureFailure));
            }

            CalloutArmStatus b1 = engine.Snapshot().Callout;
            TransparentProxyDiagnostics p1 = engine.Snapshot().ProxyDiagnostics;
            ProbeRedirectCaseResult caseB = await RunProbeRedirectCase(
                engine, shortExe, WfpAppIdentityPathMode.ShortPathOnly, shortExe, url, b1, p1, ct, longExe, shortExe).ConfigureAwait(false);
            if (caseB.InfrastructureFailure is not null)
            {
                return Fail(
                    "wfp-probe-short-appid-ab",
                    ProbeInfrastructureFailure(caseB.InfrastructureFailure));
            }

            bool aMatched = ProbeAbCaseMatched(caseA, baselineDirectPublicIp);
            bool bMatched = ProbeAbCaseMatched(caseB, baselineDirectPublicIp);
            string experimentResult = aMatched
                ? "A_MATCHES"
                : bMatched
                    ? "B_ONLY"
                    : "NO_MATCH";
            string interpretation = aMatched
                ? "SHORT APP_ID filter matches LONG launch; dual APP_ID production workaround is viable."
                : bMatched
                    ? "SHORT APP_ID matches only SHORT launch; does not fix normal Unicode/Telegram launch path."
                    : "8.3 APP_ID workaround not confirmed.";

            string detail =
                "A_MATCHED=" + aMatched
                + " B_MATCHED=" + bMatched
                + " experimentResult=" + experimentResult
                + " baselineDirectPublicIp=" + baselineDirectPublicIp
                + " | A(shortFilter,longLaunch)=" + FormatProbeRedirectCase("A", caseA)
                + " | B(shortFilter,shortLaunch)=" + FormatProbeRedirectCase("B", caseB)
                + " | " + interpretation;

            if (aMatched)
            {
                return Pass("wfp-probe-short-appid-ab", detail);
            }

            return Warn("wfp-probe-short-appid-ab", detail);
        }
        finally
        {
            await engine.RemoveTempAppVpnRouteAsync().ConfigureAwait(false);
        }
    }

    private static async Task<(DiagnosticResult? Fail, string? DirectPublicIp)> EnsureProbeHttpSanityAsync(
        string diagnosticName,
        string exePath,
        string url,
        CancellationToken ct)
    {
        ProbeRunResult run = await RunProbe(exePath, ["--http", url], ct).ConfigureAwait(false);
        if (ProbeLaunchFailure(diagnosticName, run) is DiagnosticResult launchFail)
        {
            return (launchFail, null);
        }

        if (!run.Output.Contains("http OK", StringComparison.Ordinal))
        {
            return (Fail(diagnosticName, ProbeInfrastructureFailure("Probe sanity: missing http OK. Output: " + run.Output.Trim())), null);
        }

        if (ParseLocal(run.Output) is null)
        {
            return (Fail(diagnosticName, ProbeInfrastructureFailure("Probe sanity: missing local endpoint. Output: " + run.Output.Trim())), null);
        }

        string? publicIp = ProbeHttpOutputParser.TryParsePublicIpv4(run.Output);
        if (publicIp is null)
        {
            return (Fail(diagnosticName, ProbeInfrastructureFailure("Probe sanity: missing public IP. Output: " + run.Output.Trim())), null);
        }

        return (null, publicIp);
    }

    private sealed record ProbeRedirectCaseResult(
        bool FiltersOk,
        string FilterDetail,
        WfpAppIdentityPathMode IdentityMode,
        string DisplayPath,
        string ShortExe,
        string IdentityInput,
        string IdentityPathUsed,
        string IdentityAppId,
        ulong FilterId,
        int RequestedVpnAppCount,
        bool CalloutMatched,
        bool ApplyModified,
        bool ProxyAccepted,
        bool RedirectContextRecovered,
        bool ProxyFlowObserved,
        string? Local,
        string? PublicIp,
        bool HttpOk,
        string? LaunchError,
        string? InfrastructureFailure);

    private static bool ProbeRedirectPass(ProbeRedirectCaseResult r, string? baselineDirectPublicIp) =>
        ProbeAbCaseMatched(r, baselineDirectPublicIp);

    private static bool ProbeAbCaseMatched(ProbeRedirectCaseResult r, string? baselineDirectPublicIp)
        => WfpProbeRedirectDiagnosticVerdict.EndToEndRoutingSucceeded(
            r.FiltersOk,
            r.HttpOk,
            r.CalloutMatched,
            r.ApplyModified,
            r.ProxyAccepted,
            r.RedirectContextRecovered,
            r.ProxyFlowObserved,
            r.Local,
            r.PublicIp,
            r.LaunchError,
            r.InfrastructureFailure,
            baselineDirectPublicIp);

    private static string FormatProbeRedirectCase(string label, ProbeRedirectCaseResult r) =>
        label + "{"
        + r.FilterDetail
        + " shortExe=" + r.ShortExe
        + " identityPathUsed=" + r.IdentityPathUsed
        + " requestedVpnApps=" + r.RequestedVpnAppCount
        + " filtersOk=" + r.FiltersOk
        + " httpOk=" + r.HttpOk
        + " classifyDelta=" + r.CalloutMatched
        + " apply=" + r.ApplyModified
        + " proxy=" + r.ProxyAccepted
        + " ctx=" + r.RedirectContextRecovered
        + " flow=" + r.ProxyFlowObserved
        + " local=" + r.Local
        + " public=" + r.PublicIp
        + (r.LaunchError is null ? "" : " launchError=" + r.LaunchError)
        + "}";

    private static async Task<ProbeRedirectCaseResult> RunProbeRedirectCase(
        RouterEngine engine,
        string applyExePath,
        WfpAppIdentityPathMode mode,
        string launchExe,
        string url,
        CalloutArmStatus calloutBefore,
        TransparentProxyDiagnostics proxyBefore,
        CancellationToken ct,
        string? abLongExe = null,
        string? abShortExe = null)
    {
        await engine.RemoveTempAppVpnRouteAsync().ConfigureAwait(false);
        TempAppVpnStatus st = await engine.ApplyTempAppVpnRouteAsync(applyExePath, mode).ConfigureAwait(false);
        string displayPath = st.ExePath ?? WfpAppIdentity.GetDisplayPath(applyExePath);
        int requestedVpnApps = engine.WfpPolicy.RequestedVpnApps;
        string expectedIdentity = mode == WfpAppIdentityPathMode.ShortPathOnly
            ? applyExePath.Trim().Trim('"').Replace('/', '\\')
            : WfpAppIdentity.GetIdentityPaths(applyExePath, mode).FirstOrDefault() ?? applyExePath;

        IReadOnlyList<WfpFilterInstallResult> filters = WfpPolicyHealth.FindCalloutFilters(engine.WfpPolicy, displayPath);
        WfpFilterInstallResult? installed = filters.FirstOrDefault(f =>
            f.FilterInstalled
            && string.Equals(f.IdentityPathUsed, expectedIdentity, StringComparison.OrdinalIgnoreCase))
            ?? filters.FirstOrDefault(f => f.FilterInstalled);
        string identityInput = installed?.IdentityPathUsed ?? expectedIdentity;
        string identityPathUsed = installed?.IdentityPathUsed ?? string.Empty;
        WfpAppIdentity.TryResolveAleAppIdFromFileName(identityInput, out string identityAppId);
        bool filtersOk = EvaluateFiltersOk(mode, displayPath, expectedIdentity, filters);
        string filterDetail = FormatInstalledFilterDiagnostic(
            mode,
            displayPath,
            identityInput,
            identityAppId,
            installed);

        string? infrastructureFailure = null;
        if (mode == WfpAppIdentityPathMode.ShortPathOnly
            && abLongExe is not null
            && abShortExe is not null)
        {
            infrastructureFailure = ValidateShortPathAbInfrastructure(
                abLongExe,
                abShortExe,
                displayPath,
                identityInput,
                identityPathUsed,
                identityAppId,
                installed,
                requestedVpnApps);
        }

        if (infrastructureFailure is not null)
        {
            return new ProbeRedirectCaseResult(
                false,
                filterDetail,
                mode,
                displayPath,
                abShortExe ?? string.Empty,
                identityInput,
                identityPathUsed,
                identityAppId,
                installed?.FilterId ?? 0,
                requestedVpnApps,
                false,
                false,
                false,
                false,
                false,
                null,
                null,
                false,
                null,
                infrastructureFailure);
        }

        ProbeRunResult run = await RunProbe(launchExe, ["--http", url], ct).ConfigureAwait(false);
        string? launchError = null;
        if (!run.Launched)
        {
            launchError = run.LaunchError ?? "Process.Start failed.";
        }
        else if (run.LooksLikeRuntimeLaunchFailure)
        {
            launchError = run.Output.Trim();
        }

        ServiceSnapshot after = engine.Snapshot();
        bool calloutMatched = after.Callout.ClassifyEntries > calloutBefore.ClassifyEntries
            || after.Callout.RedirectAttempts > calloutBefore.RedirectAttempts;
        bool applyModified = after.Callout.RedirectApplySuccess > calloutBefore.RedirectApplySuccess;
        bool proxyAccepted = after.ProxyDiagnostics.AcceptedConnections > proxyBefore.AcceptedConnections;
        bool ctx = after.ProxyDiagnostics.RedirectContextSuccess > proxyBefore.RedirectContextSuccess;
        string? local = run.Launched ? ParseLocal(run.Output) : null;
        string? pub = run.Launched ? ProbeHttpOutputParser.TryParsePublicIpv4(run.Output) : null;
        bool httpOk = run.Launched && run.Output.Contains("http OK", StringComparison.Ordinal);
        bool flow = engine.Proxy?.Flows.Any(f =>
            string.Equals(f.ProcessPath, Path.GetFullPath(launchExe), StringComparison.OrdinalIgnoreCase) && f.WfpRedirect) == true;

        return new ProbeRedirectCaseResult(
            filtersOk,
            filterDetail,
            mode,
            displayPath,
            abShortExe ?? string.Empty,
            identityInput,
            identityPathUsed,
            identityAppId,
            installed?.FilterId ?? 0,
            requestedVpnApps,
            calloutMatched,
            applyModified,
            proxyAccepted,
            ctx,
            flow,
            local,
            pub,
            httpOk,
            launchError,
            null);
    }

    private static string? ValidateShortPathAbInfrastructure(
        string longExe,
        string shortExe,
        string displayPath,
        string identityInput,
        string identityPathUsed,
        string identityAppId,
        WfpFilterInstallResult? installed,
        int requestedVpnAppCount)
    {
        if (string.Equals(shortExe, longExe, StringComparison.OrdinalIgnoreCase))
        {
            return "shortExe equals longExe; 8.3 path required.";
        }

        if (WfpAppIdentity.ContainsNonAscii(shortExe))
        {
            return "shortExe is not fully ASCII: " + shortExe;
        }

        if (!string.Equals(identityInput, shortExe, StringComparison.OrdinalIgnoreCase))
        {
            return "identityInput != shortExe (override lost?). identityInput=" + identityInput
                + " shortExe=" + shortExe
                + " requestedVpnApps=" + requestedVpnAppCount;
        }

        if (installed is null
            || !string.Equals(installed.IdentityPathUsed, shortExe, StringComparison.OrdinalIgnoreCase))
        {
            return "installed filter IdentityPathUsed != shortExe. used=" + identityPathUsed
                + " shortExe=" + shortExe;
        }

        if (WfpAppIdentity.ContainsNonAscii(identityAppId))
        {
            return "identityAppId still contains Unicode (long path APP_ID). appId=" + identityAppId;
        }

        if (!identityAppId.Contains('~', StringComparison.Ordinal))
        {
            return "identityAppId missing 8.3 tilde marker. appId=" + identityAppId;
        }

        return null;
    }

    private static bool EvaluateFiltersOk(
        WfpAppIdentityPathMode mode,
        string displayPath,
        string expectedIdentityInput,
        IReadOnlyList<WfpFilterInstallResult> filters)
        => WfpProbeRedirectDiagnosticVerdict.InstalledFiltersMeetExpectation(mode, expectedIdentityInput, filters);

    private static string FormatInstalledFilterDiagnostic(
        WfpAppIdentityPathMode mode,
        string displayPath,
        string identityInput,
        string identityAppId,
        WfpFilterInstallResult? filter)
    {
        string role = mode switch
        {
            WfpAppIdentityPathMode.ShortPathOnly => "short-path-only",
            WfpAppIdentityPathMode.LongPathOnly => "long-path-only",
            _ => filter?.IsShortPathFallback == true ? "short-fallback" : "long-primary",
        };

        return "displayPath=" + displayPath
            + " identityInput=" + identityInput
            + " identityAppId=" + (string.IsNullOrEmpty(identityAppId) ? "(unresolved)" : identityAppId)
            + " filterId=" + (filter?.FilterId ?? 0)
            + " identityMode=" + mode
            + " filterRole=" + role;
    }

    private static string AsciiProbePublicDirectory()
    {
        string publicRoot = Environment.GetEnvironmentVariable("PUBLIC") ?? @"C:\Users\Public";
        return Path.Combine(publicRoot, "SVRProbeAscii");
    }

    private static async Task<DiagnosticResult> RunWfpRuntimeAppIdCapture(RouterEngine engine, CancellationToken ct)
    {
        const string name = "wfp-runtime-appid-capture";
        if (FindProbe() is null)
        {
            return Fail(name, Bilingual("Probe.exe missing.", "Probe.exe ne naiden."));
        }

        WfpSession? wfp = engine.Wfp;
        if (wfp is not { SessionOpen: true })
        {
            return Fail(name, Bilingual(
                "Production WFP session is not open. Connect VPN Route first.",
                "WFP session ne otkryta. Snachala Connect."));
        }

        CalloutDriverClient? driver = engine.Driver;
        if (driver is not { IsLoaded: true })
        {
            return Fail(name, Bilingual("Callout driver not loaded.", "Callout-draiver ne zagruzhen."));
        }

        if (driver.TryGetStatus(out CalloutArmStatus baseline, out string statusErr)
            && baseline.StatusStructVersion != CalloutDriverClient.ExpectedStatusStructVersion)
        {
            return Fail(name, Bilingual(
                "Driver ABI mismatch: rebuild and reinstall .sys (build-driver.ps1 + install-driver.ps1). StatusStructVersion="
                + baseline.StatusStructVersion,
                "Nesovpadenie ABI draivera."));
        }

        if (!string.IsNullOrEmpty(statusErr))
        {
            return Fail(name, statusErr);
        }

        string unicodeLongExe;
        string asciiExe;
        try
        {
            unicodeLongExe = Path.GetFullPath(ProbeCopyHelper.PrepareProbeCopy(WfpUnicodeProbeDirectory()));
            asciiExe = Path.GetFullPath(ProbeCopyHelper.PrepareProbeCopy(AsciiProbePublicDirectory()));
        }
        catch (Exception ex)
        {
            return Fail(name, ProbeInfrastructureFailure(ex.Message));
        }

        if (!WfpAppIdentity.TryResolveAleAppIdFromFileName(asciiExe, out string asciiFwpmAppId, out uint asciiFwpmBytes)
            || !WfpAppIdentity.TryResolveAleAppIdFromFileName(unicodeLongExe, out string longFwpmAppId, out uint longFwpmBytes))
        {
            return Fail(name, Bilingual(
                "FwpmGetAppIdFromFileName0 failed for probe paths.",
                "FwpmGetAppIdFromFileName0 ne udalos."));
        }

        string[] tcpArgs =
        [
            "--tcp",
            WfpRuntimeAppIdCapture.DiagnosticRemoteHost,
            WfpRuntimeAppIdCapture.DiagnosticRemotePort.ToString(),
        ];

        using var captureFilter = new WfpRuntimeAppIdCapture(wfp);
        if (!captureFilter.TryInstall(out string installErr))
        {
            return Fail(name, Bilingual(
                "Could not install scoped diagnostic capture filter: " + installErr,
                "Ne udalos ustanovit diagnosticheskiy filter: " + installErr));
        }

        string header = WfpRuntimeAppIdCapture.FormatRemoteAddressEncodingReport()
            + "\nreusedProductionWfpSession=True"
            + " | filterAddStatus=0x" + captureFilter.FilterAddStatus.ToString("X8")
            + " | diagnosticFilterId=" + captureFilter.DiagnosticFilterId;

        try
        {
            RuntimeAppIdCaptureRow asciiRow = await RunRuntimeAppIdCaptureCase(
                driver, asciiExe, asciiFwpmAppId, asciiFwpmBytes, tcpArgs, ct).ConfigureAwait(false);
            if (asciiRow.InfrastructureError is not null)
            {
                return Fail(name, asciiRow.InfrastructureError + "\n" + header);
            }

            if (!asciiRow.EqOrdinal)
            {
                return Fail(name, Bilingual(
                    "ASCII control: runtime ALE_APP_ID != FwpmGetAppIdFromFileName0 (ordinal).",
                    "ASCII kontrol ne proshol (ordinal).")
                    + "\n" + header + "\n" + FormatRuntimeCaptureTable(asciiRow, null));
            }

            RuntimeAppIdCaptureRow unicodeLongRow = await RunRuntimeAppIdCaptureCase(
                driver, unicodeLongExe, longFwpmAppId, longFwpmBytes, tcpArgs, ct).ConfigureAwait(false);
            if (unicodeLongRow.InfrastructureError is not null)
            {
                return Fail(name, unicodeLongRow.InfrastructureError + "\n" + header);
            }

            bool anyCapture = asciiRow.CaptureCountDelta > 0
                || unicodeLongRow.CaptureCountDelta > 0;
            string report = header + "\n" + FormatRuntimeCaptureTable(asciiRow, unicodeLongRow);

            if (!anyCapture)
            {
                return Fail(name, Bilingual(
                    "No runtime captures recorded.",
                    "Zahvatov net.") + "\n" + report);
            }

            return Pass(name, Bilingual(
                "Runtime APP_ID capture complete (network-order IP_REMOTE_ADDRESS).",
                "Zakhvat zavershen.") + "\n" + report);
        }
        finally
        {
            captureFilter.Dispose();
        }
    }

    private static async Task<DiagnosticResult> RunWfpRuntimeAppIdCase(RouterEngine engine, CancellationToken ct)
    {
        const string name = "wfp-runtime-appid-case";
        try
        {
            return await RunWfpRuntimeAppIdCaseCore(engine, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            engine.Log(name + " infrastructure exception: " + ex);
            return Fail(name, Bilingual(
                "Diagnostic infrastructure exception (see stack trace below).",
                "Isklyuchenie infrastruktury diagnostiki.")
                + "\n--- exception ---\n"
                + ex);
        }
    }

    private static async Task<DiagnosticResult> RunWfpRuntimeAppIdCaseCore(RouterEngine engine, CancellationToken ct)
    {
        const string name = "wfp-runtime-appid-case";
        if (FindProbe() is null)
        {
            return Fail(name, Bilingual("Probe.exe missing.", "Probe.exe ne naiden."));
        }

        WfpSession? wfp = engine.Wfp;
        if (wfp is not { SessionOpen: true })
        {
            return Fail(name, Bilingual(
                "Production WFP session is not open. Connect VPN Route first.",
                "WFP session ne otkryta."));
        }

        CalloutDriverClient? driver = engine.Driver;
        if (driver is not { IsLoaded: true })
        {
            return Fail(name, Bilingual("Callout driver not loaded.", "Callout-draiver ne zagruzhen."));
        }

        string unicodeLongExe;
        string asciiExe;
        try
        {
            unicodeLongExe = Path.GetFullPath(ProbeCopyHelper.PrepareProbeCopy(WfpUnicodeProbeDirectory()));
            asciiExe = Path.GetFullPath(ProbeCopyHelper.PrepareProbeCopy(AsciiProbePublicDirectory()));
        }
        catch (Exception ex)
        {
            return Fail(name, ProbeInfrastructureFailure(ex.Message));
        }

        string unicodeLowerInputPath = unicodeLongExe.ToLowerInvariant();

        if (!WfpAppIdentity.TryResolveAleAppIdFromFileName(unicodeLongExe, out string fwpmOriginal, out uint fwpmOriginalBytes)
            || !WfpAppIdentity.TryResolveAleAppIdFromFileName(unicodeLowerInputPath, out string fwpmLowerInput, out uint fwpmLowerBytes))
        {
            return Fail(name, Bilingual(
                "FwpmGetAppIdFromFileName0 failed for Unicode probe paths.",
                "FwpmGetAppIdFromFileName0 ne udalos."));
        }

        string[] tcpArgs =
        [
            "--tcp",
            WfpRuntimeAppIdCapture.DiagnosticRemoteHost,
            WfpRuntimeAppIdCapture.DiagnosticRemotePort.ToString(),
        ];

        var sb = new System.Text.StringBuilder();
        sb.AppendLine(WfpRuntimeAppIdCapture.FormatRemoteAddressEncodingReport());
        sb.AppendLine("reusedProductionWfpSession=True");
        sb.AppendLine("unicodeLongExe=" + unicodeLongExe);
        sb.AppendLine("unicodeLowerInputPath=" + unicodeLowerInputPath);
        sb.AppendLine();

        using (var captureFilter = new WfpRuntimeAppIdCapture(wfp))
        {
            if (!captureFilter.TryInstall(out string installErr))
            {
                return Fail(name, Bilingual(
                    "Could not install base capture filter: " + installErr,
                    installErr));
            }

            RuntimeAppIdCaptureRow unicodeRuntimeRow = await RunRuntimeAppIdCaptureCase(
                driver,
                unicodeLongExe,
                fwpmOriginal,
                fwpmOriginalBytes,
                tcpArgs,
                ct).ConfigureAwait(false);
            if (unicodeRuntimeRow.InfrastructureError is not null)
            {
                return Fail(name, unicodeRuntimeRow.InfrastructureError + "\n" + sb);
            }

            string runtimeAppId = unicodeRuntimeRow.RuntimeAleAppId;
            bool originalEqRuntimeOrdinal = WfpAleAppIdComparison.EqualsOrdinal(fwpmOriginal, runtimeAppId);
            bool lowerInputEqRuntimeOrdinal = WfpAleAppIdComparison.EqualsOrdinal(fwpmLowerInput, runtimeAppId);
            WfpAleAppIdDiffDetail originalDiff = WfpAleAppIdComparison.Analyze(
                fwpmOriginal,
                runtimeAppId,
                fwpmOriginalBytes,
                unicodeRuntimeRow.RuntimeAleAppIdByteLength);
            WfpAleAppIdDiffDetail lowerDiff = WfpAleAppIdComparison.Analyze(
                fwpmLowerInput,
                runtimeAppId,
                fwpmLowerBytes,
                unicodeRuntimeRow.RuntimeAleAppIdByteLength);

            sb.AppendLine("=== Fwpm APP_ID vs runtime (no ALE_APP_ID on filter) ===");
            sb.AppendLine("fwpmOriginal=" + fwpmOriginal);
            sb.AppendLine("fwpmLowerInput=" + fwpmLowerInput);
            sb.AppendLine("runtimeAppId=" + runtimeAppId);
            sb.AppendLine("originalEqRuntimeOrdinal=" + originalEqRuntimeOrdinal);
            sb.AppendLine("lowerInputEqRuntimeOrdinal=" + lowerInputEqRuntimeOrdinal);
            sb.AppendLine("originalDiff: " + WfpAleAppIdComparison.FormatDiffReport(originalDiff));
            sb.AppendLine("lowerInputDiff: " + WfpAleAppIdComparison.FormatDiffReport(lowerDiff));
            sb.AppendLine();
        }

        AppIdFilterExperimentRow asciiFilter = await RunAppIdFilterExperimentAsync(
            wfp,
            driver,
            asciiExe,
            "ASCII",
            "Fwpm original ASCII path",
            asciiExe,
            tcpArgs,
            ct).ConfigureAwait(false);
        if (asciiFilter.InfrastructureError is not null)
        {
            return Fail(name, asciiFilter.InfrastructureError + "\n" + sb);
        }

        if (asciiFilter.CaptureDelta == 0)
        {
            return Fail(name, Bilingual(
                "ASCII APP_ID filter control: delta=0 — experiment invalid.",
                "ASCII kontrol delta=0.")
                + "\n" + sb + FormatAppIdFilterExperimentTable(asciiFilter, null, null));
        }

        AppIdFilterExperimentRow unicodeOriginalFilter = await RunAppIdFilterExperimentAsync(
            wfp,
            driver,
            unicodeLongExe,
            "Unicode original",
            "Fwpm original Unicode path",
            unicodeLongExe,
            tcpArgs,
            ct).ConfigureAwait(false);
        if (unicodeOriginalFilter.InfrastructureError is not null)
        {
            return Fail(name, unicodeOriginalFilter.InfrastructureError + "\n" + sb);
        }

        AppIdFilterExperimentRow unicodeLowerFilter = await RunAppIdFilterExperimentAsync(
            wfp,
            driver,
            unicodeLowerInputPath,
            "Unicode lower",
            "Fwpm lowercased input path",
            unicodeLongExe,
            tcpArgs,
            ct).ConfigureAwait(false);
        if (unicodeLowerFilter.InfrastructureError is not null)
        {
            return Fail(name, unicodeLowerFilter.InfrastructureError + "\n" + sb);
        }

        sb.AppendLine("=== ALE_APP_ID filter experiment (TCP+port+address+APP_ID) ===");
        sb.AppendLine(FormatAppIdFilterExperimentTable(asciiFilter, unicodeOriginalFilter, unicodeLowerFilter));
        sb.AppendLine();
        sb.AppendLine(InterpretAppIdFilterExperiment(asciiFilter, unicodeOriginalFilter, unicodeLowerFilter));

        return Pass(name, Bilingual(
            "APP_ID case diagnostic complete.",
            "Diagnostika APP_ID zavershena.") + "\n" + sb);
    }

    private sealed record AppIdFilterExperimentRow(
        string CaseLabel,
        string AppIdSource,
        string FwpmInputPath,
        string AppIdText,
        uint AppIdByteLength,
        uint CaptureDelta,
        string RuntimeAppId,
        uint RuntimeAppIdByteLength,
        bool OrdinalEqual,
        bool OrdinalIgnoreCaseEqual,
        WfpAleAppIdDiffDetail Diff,
        string? InfrastructureError);

    private static async Task<AppIdFilterExperimentRow> RunAppIdFilterExperimentAsync(
        WfpSession wfp,
        CalloutDriverClient driver,
        string fwpmInputPath,
        string caseLabel,
        string appIdSource,
        string probeLaunchPath,
        IReadOnlyList<string> tcpArgs,
        CancellationToken ct)
    {
        if (!WfpAppIdentity.TryAcquireAleAppIdBlob(fwpmInputPath, out IntPtr appIdBlob, out uint fwpmStatus))
        {
            return new AppIdFilterExperimentRow(
                caseLabel,
                appIdSource,
                fwpmInputPath,
                "",
                0,
                0,
                "",
                0,
                false,
                false,
                WfpAleAppIdComparison.Analyze("", ""),
                Bilingual(
                    "FwpmGetAppIdFromFileName0 failed: 0x" + fwpmStatus.ToString("X8") + " path=" + fwpmInputPath,
                    "FwpmGetAppIdFromFileName0 ne udalos."));
        }

        if (!WfpAppIdentity.TryDecodeAleAppIdBlob(appIdBlob, out string appIdText, out uint appIdByteLength))
        {
            WfpAppIdentity.FreeAleAppIdBlob(ref appIdBlob);
            return new AppIdFilterExperimentRow(
                caseLabel,
                appIdSource,
                fwpmInputPath,
                "",
                0,
                0,
                "",
                0,
                false,
                false,
                WfpAleAppIdComparison.Analyze("", ""),
                Bilingual("APP_ID blob decode failed.", "Ne udalos dekodirovat APP_ID."));
        }

        return await RunAppIdFilterExperimentWithBlobAsync(
            wfp,
            driver,
            appIdBlob,
            diagnosticOwnedBlob: false,
            caseLabel,
            appIdSource,
            fwpmInputPath,
            appIdText,
            appIdByteLength,
            probeLaunchPath,
            tcpArgs,
            ct).ConfigureAwait(false);
    }

    private static async Task<AppIdFilterExperimentRow> RunAppIdFilterExperimentWithBlobAsync(
        WfpSession wfp,
        CalloutDriverClient driver,
        IntPtr appIdBlob,
        bool diagnosticOwnedBlob,
        string caseLabel,
        string appIdSource,
        string sourcePathLabel,
        string appIdText,
        uint appIdByteLength,
        string probeLaunchPath,
        IReadOnlyList<string> tcpArgs,
        CancellationToken ct)
    {
        if (!wfp.TryAddRuntimeAppIdCaptureFilterWithAppId(appIdBlob, out WfpRuntimeCaptureFilterResult add)
            || !add.Success)
        {
            FreeAppIdExperimentBlob(ref appIdBlob, diagnosticOwnedBlob);
            return new AppIdFilterExperimentRow(
                caseLabel,
                appIdSource,
                sourcePathLabel,
                appIdText,
                appIdByteLength,
                0,
                "",
                0,
                false,
                false,
                WfpAleAppIdComparison.Analyze(appIdText, ""),
                Bilingual(
                    "Could not install APP_ID capture filter: " + (add.Error ?? "0x" + add.FilterAddStatus.ToString("X8")),
                    "Ne udalos ustanovit filter."));
        }

        FreeAppIdExperimentBlob(ref appIdBlob, diagnosticOwnedBlob);

        try
        {
            if (!driver.TryResetRuntimeCapture(out string resetErr))
            {
                return new AppIdFilterExperimentRow(
                    caseLabel, appIdSource, sourcePathLabel, appIdText, appIdByteLength,
                    0, "", 0, false, false, WfpAleAppIdComparison.Analyze(appIdText, ""),
                    Bilingual("Reset runtime capture failed: " + resetErr, resetErr));
            }

            uint countBefore = 0;
            if (driver.TryGetStatus(out CalloutArmStatus before, out _))
            {
                countBefore = before.RuntimeCaptureCount;
            }

            ProbeRunResult run = await RunProbe(probeLaunchPath, tcpArgs, ct).ConfigureAwait(false);
            if (!run.Launched || run.LooksLikeRuntimeLaunchFailure)
            {
                return new AppIdFilterExperimentRow(
                    caseLabel, appIdSource, sourcePathLabel, appIdText, appIdByteLength,
                    0, "", 0, false, false, WfpAleAppIdComparison.Analyze(appIdText, ""),
                    ProbeInfrastructureFailure(run.LaunchError ?? run.Output.Trim()));
            }

            await Task.Delay(400, ct).ConfigureAwait(false);

            if (!driver.TryGetStatus(out CalloutArmStatus after, out string getErr))
            {
                return new AppIdFilterExperimentRow(
                    caseLabel, appIdSource, sourcePathLabel, appIdText, appIdByteLength,
                    0, "", 0, false, false, WfpAleAppIdComparison.Analyze(appIdText, ""),
                    getErr);
            }

            uint delta = after.RuntimeCaptureCount - countBefore;
            string runtime = after.RuntimeAppId;
            bool eqOrd = WfpAleAppIdComparison.EqualsOrdinal(appIdText, runtime);
            bool eqIgn = WfpAleAppIdComparison.EqualsOrdinalIgnoreCase(appIdText, runtime);
            WfpAleAppIdDiffDetail diff = WfpAleAppIdComparison.Analyze(
                appIdText,
                runtime,
                appIdByteLength,
                after.RuntimeAppIdByteLength);

            return new AppIdFilterExperimentRow(
                caseLabel,
                appIdSource,
                sourcePathLabel,
                appIdText,
                appIdByteLength,
                delta,
                runtime,
                after.RuntimeAppIdByteLength,
                eqOrd,
                eqIgn,
                diff,
                null);
        }
        finally
        {
            wfp.RemoveRuntimeAppIdCaptureFilter(add.FilterKey);
        }
    }

    private static void FreeAppIdExperimentBlob(ref IntPtr appIdBlob, bool diagnosticOwnedBlob)
    {
        if (appIdBlob == IntPtr.Zero)
        {
            return;
        }

        if (diagnosticOwnedBlob)
        {
            WfpDiagnosticAleAppIdBlob.FreeDiagnosticAleAppIdBlob(ref appIdBlob);
        }
        else
        {
            WfpAppIdentity.FreeAleAppIdBlob(ref appIdBlob);
        }
    }

    private static string FormatAppIdFilterExperimentTable(
        AppIdFilterExperimentRow ascii,
        AppIdFilterExperimentRow? unicodeOriginal,
        AppIdFilterExperimentRow? unicodeLower)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("| Case | APP_ID source | appId | delta | runtimeAppId | ordinalEqual | eqOrdinalIgnoreCase |");
        sb.AppendLine("|------|---------------|-------|-------|--------------|--------------|---------------------|");
        sb.AppendLine(FormatAppIdFilterExperimentRow(ascii));
        if (unicodeOriginal is not null)
        {
            sb.AppendLine(FormatAppIdFilterExperimentRow(unicodeOriginal));
        }

        if (unicodeLower is not null)
        {
            sb.AppendLine(FormatAppIdFilterExperimentRow(unicodeLower));
        }

        sb.AppendLine();
        sb.AppendLine("ASCII diff: " + WfpAleAppIdComparison.FormatDiffReport(ascii.Diff));
        if (unicodeOriginal is not null)
        {
            sb.AppendLine("Unicode original diff: " + WfpAleAppIdComparison.FormatDiffReport(unicodeOriginal.Diff));
        }

        if (unicodeLower is not null)
        {
            sb.AppendLine("Unicode lower diff: " + WfpAleAppIdComparison.FormatDiffReport(unicodeLower.Diff));
        }

        return sb.ToString();
    }

    private static string FormatAppIdFilterExperimentRow(AppIdFilterExperimentRow row)
        => "| " + row.CaseLabel
           + " | " + EscapeMd(row.AppIdSource)
           + " | " + EscapeMd(row.AppIdText)
           + " | " + row.CaptureDelta
           + " | " + EscapeMd(row.RuntimeAppId)
           + " | " + row.OrdinalEqual
           + " | " + row.OrdinalIgnoreCaseEqual
           + " |";

    private static string InterpretAppIdFilterExperiment(
        AppIdFilterExperimentRow ascii,
        AppIdFilterExperimentRow unicodeOriginal,
        AppIdFilterExperimentRow unicodeLower)
    {
        if (ascii.CaptureDelta == 0)
        {
            return "interpretation: ASCII control delta=0 — do not interpret Unicode results.";
        }

        bool caseHypothesis = unicodeOriginal.CaptureDelta == 0 && unicodeLower.CaptureDelta > 0;
        if (caseHypothesis)
        {
            return "interpretation: ASCII delta>0, Unicode original delta=0, Unicode lower delta>0"
                + " => STRONG EVIDENCE for Unicode case mismatch between FwpmGetAppIdFromFileName0(original path)"
                + " and runtime FWPS ALE_APP_ID (lowercase). Production fix design only after confirming on Telegram path.";
        }

        if (unicodeOriginal.CaptureDelta > 0 && unicodeLower.CaptureDelta > 0)
        {
            return "interpretation: both Unicode original and lower APP_ID filters matched (delta>0)."
                + " Case-only mismatch is NOT supported by filter experiment; report ordinal diff facts above.";
        }

        if (unicodeOriginal.CaptureDelta == 0 && unicodeLower.CaptureDelta == 0)
        {
            if (string.Equals(unicodeOriginal.AppIdText, unicodeLower.AppIdText, StringComparison.Ordinal)
                && !unicodeOriginal.OrdinalEqual)
            {
                return "interpretation: Fwpm original != runtime (ordinal), and lower-filesystem-input Fwpm APP_ID"
                    + " equals original Fwpm blob — lowercasing filesystem input is ineffective because"
                    + " FwpmGetAppIdFromFileName0 restores canonical filesystem casing."
                    + " Use manual/production normalized APP_ID blob (ToLowerInvariant on canonical string)"
                    + " as validated by wfp-runtime-appid-blob.";
            }

            return "interpretation: both Unicode filters missed (delta=0);"
                + " investigate APP_ID blob content, filter install, or traffic path."
                + " Do NOT conclude 'case hypothesis not confirmed' if blob diagnostic proved casing mismatch.";
        }

        return "interpretation: mixed results — original delta=" + unicodeOriginal.CaptureDelta
            + " lower delta=" + unicodeLower.CaptureDelta
            + "; no case-only conclusion.";
    }

    private static async Task<DiagnosticResult> RunWfpRuntimeAppIdBlob(RouterEngine engine, CancellationToken ct)
    {
        const string name = "wfp-runtime-appid-blob";
        try
        {
            return await RunWfpRuntimeAppIdBlobCore(engine, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            engine.Log(name + " infrastructure exception: " + ex);
            return Fail(name, Bilingual(
                "Diagnostic infrastructure exception (see stack trace below).",
                "Isklyuchenie infrastruktury diagnostiki.")
                + "\n--- exception ---\n"
                + ex);
        }
    }

    private static async Task<DiagnosticResult> RunWfpRuntimeAppIdBlobCore(RouterEngine engine, CancellationToken ct)
    {
        const string name = "wfp-runtime-appid-blob";
        if (FindProbe() is null)
        {
            return Fail(name, Bilingual("Probe.exe missing.", "Probe.exe ne naiden."));
        }

        WfpSession? wfp = engine.Wfp;
        if (wfp is not { SessionOpen: true })
        {
            return Fail(name, Bilingual(
                "Production WFP session is not open. Connect VPN Route first.",
                "WFP session ne otkryta."));
        }

        CalloutDriverClient? driver = engine.Driver;
        if (driver is not { IsLoaded: true })
        {
            return Fail(name, Bilingual("Callout driver not loaded.", "Callout-draiver ne zagruzhen."));
        }

        string unicodeLongExe;
        string asciiExe;
        try
        {
            unicodeLongExe = Path.GetFullPath(ProbeCopyHelper.PrepareProbeCopy(WfpUnicodeProbeDirectory()));
            asciiExe = Path.GetFullPath(ProbeCopyHelper.PrepareProbeCopy(AsciiProbePublicDirectory()));
        }
        catch (Exception ex)
        {
            return Fail(name, ProbeInfrastructureFailure(ex.Message));
        }

        if (!WfpAppIdentity.TryResolveAleAppIdFromFileName(unicodeLongExe, out string fwpmOriginal, out uint fwpmOriginalByteLength))
        {
            return Fail(name, Bilingual("FwpmGetAppIdFromFileName0 failed for Unicode path.", "Fwpm ne udalos."));
        }

        string[] tcpArgs =
        [
            "--tcp",
            WfpRuntimeAppIdCapture.DiagnosticRemoteHost,
            WfpRuntimeAppIdCapture.DiagnosticRemotePort.ToString(),
        ];

        var sb = new System.Text.StringBuilder();
        sb.AppendLine(WfpRuntimeAppIdCapture.FormatRemoteAddressEncodingReport());
        sb.AppendLine("reusedProductionWfpSession=True");
        sb.AppendLine("unicodeLongExe=" + unicodeLongExe);
        sb.AppendLine();

        string runtimeAppId;
        uint runtimeAppIdByteLength;
        using (var captureFilter = new WfpRuntimeAppIdCapture(wfp))
        {
            if (!captureFilter.TryInstall(out string installErr))
            {
                return Fail(name, Bilingual("Base capture filter failed: " + installErr, installErr));
            }

            RuntimeAppIdCaptureRow baseline = await RunRuntimeAppIdCaptureCase(
                driver,
                unicodeLongExe,
                fwpmOriginal,
                fwpmOriginalByteLength,
                tcpArgs,
                ct).ConfigureAwait(false);
            if (baseline.InfrastructureError is not null)
            {
                return Fail(name, baseline.InfrastructureError + "\n" + sb);
            }

            runtimeAppId = baseline.RuntimeAleAppId;
            runtimeAppIdByteLength = baseline.RuntimeAleAppIdByteLength;
        }

        string lowerCanonical = fwpmOriginal.ToLowerInvariant();
        byte[] fwpmOriginalBytes = WfpAppIdentity.TryAcquireAleAppIdBlob(unicodeLongExe, out IntPtr fwpmBlobPtr, out _)
            && WfpDiagnosticAleAppIdBlob.TryCopyBlobBytes(fwpmBlobPtr, out byte[]? copiedFwpm)
            ? copiedFwpm
            : [];
        if (fwpmBlobPtr != IntPtr.Zero)
        {
            WfpAppIdentity.FreeAleAppIdBlob(ref fwpmBlobPtr);
        }

        byte[] manualRuntimeBytes = WfpDiagnosticAleAppIdBlob.GetUtf16LeBytesIncludingTerminator(runtimeAppId);
        byte[] manualLowerBytes = WfpDiagnosticAleAppIdBlob.GetUtf16LeBytesIncludingTerminator(lowerCanonical);
        uint manualRuntimeByteLength = (uint)manualRuntimeBytes.Length;
        uint manualLowerByteLength = (uint)manualLowerBytes.Length;

        bool fwpmVsRuntimeOrdinal = WfpAleAppIdComparison.EqualsOrdinal(fwpmOriginal, runtimeAppId);
        bool lowerCanonicalVsRuntimeOrdinal = string.Equals(
            lowerCanonical,
            runtimeAppId,
            StringComparison.Ordinal);
        bool manualLowerEqualsRuntimeBlob = WfpDiagnosticAleAppIdBlob.BytesEqual(manualLowerBytes, manualRuntimeBytes);
        bool manualRuntimeMatchesExpectedEncoding = WfpDiagnosticAleAppIdBlob.BytesEqual(
            manualRuntimeBytes,
            WfpDiagnosticAleAppIdBlob.GetUtf16LeBytesIncludingTerminator(runtimeAppId));

        sb.AppendLine("=== Byte-exact APP_ID content ===");
        sb.AppendLine("fwpmOriginalString=" + fwpmOriginal);
        sb.AppendLine("runtimeString=" + runtimeAppId);
        sb.AppendLine("lowerCanonicalString=" + lowerCanonical);
        sb.AppendLine("fwpmOriginalByteLength=" + fwpmOriginalByteLength);
        sb.AppendLine("runtimeByteLength=" + runtimeAppIdByteLength);
        sb.AppendLine("manualRuntimeByteLength=" + manualRuntimeByteLength);
        sb.AppendLine("manualLowerByteLength=" + manualLowerByteLength);
        sb.AppendLine("fwpmVsRuntimeOrdinal=" + fwpmVsRuntimeOrdinal);
        sb.AppendLine("lowerCanonicalVsRuntimeOrdinal=" + lowerCanonicalVsRuntimeOrdinal);
        sb.AppendLine("manualLowerEqualsRuntimeBlob=" + manualLowerEqualsRuntimeBlob);
        sb.AppendLine("manualRuntimeMatchesExpectedEncoding=" + manualRuntimeMatchesExpectedEncoding);
        sb.AppendLine("fwpmOriginalBlobVsManualRuntimeBytes=" + WfpDiagnosticAleAppIdBlob.BytesEqual(fwpmOriginalBytes, manualRuntimeBytes));
        sb.AppendLine("byteDiffFwpmVsManualRuntime=" + WfpDiagnosticAleAppIdBlob.FormatFirstByteDifferences(fwpmOriginalBytes, manualRuntimeBytes));
        sb.AppendLine("byteDiffManualLowerVsManualRuntime=" + WfpDiagnosticAleAppIdBlob.FormatFirstByteDifferences(manualLowerBytes, manualRuntimeBytes));
        sb.AppendLine(WfpAleAppIdComparison.FormatDiffReport(WfpAleAppIdComparison.Analyze(fwpmOriginal, runtimeAppId, fwpmOriginalByteLength, runtimeAppIdByteLength)));
        sb.AppendLine(WfpAleAppIdComparison.FormatDiffReport(WfpAleAppIdComparison.Analyze(lowerCanonical, runtimeAppId, manualLowerByteLength, runtimeAppIdByteLength)));
        sb.AppendLine();

        if (!lowerCanonicalVsRuntimeOrdinal && !manualLowerEqualsRuntimeBlob)
        {
            sb.AppendLine("NOTE: lower canonical string/blob is NOT byte-identical to runtime APP_ID; manual-lower filter result must be interpreted cautiously.");
            sb.AppendLine();
        }

        AppIdFilterExperimentRow caseA = await RunAppIdFilterExperimentAsync(
            wfp, driver, asciiExe, "ASCII", "Fwpm original ASCII path", asciiExe, tcpArgs, ct).ConfigureAwait(false);
        if (caseA.InfrastructureError is not null)
        {
            return Fail(name, caseA.InfrastructureError + "\n" + sb);
        }

        if (caseA.CaptureDelta == 0)
        {
            return Fail(name, Bilingual("ASCII control delta=0 — experiment invalid.", "ASCII delta=0.") + "\n" + sb);
        }

        AppIdFilterExperimentRow caseB = await RunAppIdFilterExperimentAsync(
            wfp,
            driver,
            unicodeLongExe,
            "Unicode Fwpm",
            "FwpmGetAppIdFromFileName0 original Unicode path",
            unicodeLongExe,
            tcpArgs,
            ct).ConfigureAwait(false);
        if (caseB.InfrastructureError is not null)
        {
            return Fail(name, caseB.InfrastructureError + "\n" + sb);
        }

        IntPtr manualRuntimeBlob = WfpDiagnosticAleAppIdBlob.BuildAleAppIdBlobFromCanonicalString(runtimeAppId, out uint runtimeBlobSize);
        AppIdFilterExperimentRow caseC = await RunAppIdFilterExperimentWithBlobAsync(
            wfp,
            driver,
            manualRuntimeBlob,
            diagnosticOwnedBlob: true,
            "Unicode runtime-exact",
            "manual UTF-16LE(runtimeAppId+NUL)",
            "(manual blob)",
            runtimeAppId,
            runtimeBlobSize,
            unicodeLongExe,
            tcpArgs,
            ct).ConfigureAwait(false);
        if (caseC.InfrastructureError is not null)
        {
            return Fail(name, caseC.InfrastructureError + "\n" + sb);
        }

        IntPtr manualLowerBlob = WfpDiagnosticAleAppIdBlob.BuildAleAppIdBlobFromCanonicalString(lowerCanonical, out uint lowerBlobSize);
        AppIdFilterExperimentRow caseD = await RunAppIdFilterExperimentWithBlobAsync(
            wfp,
            driver,
            manualLowerBlob,
            diagnosticOwnedBlob: true,
            "Unicode manual-lower",
            "manual UTF-16LE(fwpmOriginal.ToLowerInvariant()+NUL)",
            "(manual blob)",
            lowerCanonical,
            lowerBlobSize,
            unicodeLongExe,
            tcpArgs,
            ct).ConfigureAwait(false);
        if (caseD.InfrastructureError is not null)
        {
            return Fail(name, caseD.InfrastructureError + "\n" + sb);
        }

        AppIdFilterExperimentRow caseE;
        if (!WfpAleAppIdBuilder.TryBuildNormalizedForFilePath(unicodeLongExe, out WfpOwnedAleAppIdBlob? productionBlob, out string? productionBuildError)
            || productionBlob is null)
        {
            caseE = new AppIdFilterExperimentRow(
                "Unicode production-normalized",
                "WfpAleAppIdBuilder",
                unicodeLongExe,
                "",
                0,
                0,
                runtimeAppId,
                runtimeAppIdByteLength,
                false,
                false,
                WfpAleAppIdComparison.Analyze("", ""),
                productionBuildError ?? "builder failed");
        }
        else
        {
            using (productionBlob)
            {
                IntPtr experimentBlob = WfpDiagnosticAleAppIdBlob.BuildAleAppIdBlobFromCanonicalString(
                    productionBlob.NormalizedAppId,
                    out uint experimentBlobSize);
                caseE = await RunAppIdFilterExperimentWithBlobAsync(
                    wfp,
                    driver,
                    experimentBlob,
                    diagnosticOwnedBlob: true,
                    "Unicode production-normalized",
                    "WfpAleAppIdBuilder (Fwpm canonical -> ToLowerInvariant -> UTF-16LE+NUL)",
                    unicodeLongExe,
                    productionBlob.NormalizedAppId,
                    experimentBlobSize,
                    unicodeLongExe,
                    tcpArgs,
                    ct).ConfigureAwait(false);
            }
        }

        if (caseE.InfrastructureError is not null)
        {
            return Fail(name, caseE.InfrastructureError + "\n" + sb);
        }

        sb.AppendLine("=== ALE_APP_ID filter experiment (A/B/C/D/E) ===");
        sb.AppendLine(FormatAppIdBlobFilterTable(caseA, caseB, caseC, caseD, caseE));
        sb.AppendLine();
        sb.AppendLine(InterpretAppIdBlobFilterExperiment(caseA, caseB, caseC, caseD, caseE, manualLowerEqualsRuntimeBlob));

        return Pass(name, Bilingual("Manual APP_ID blob experiment complete.", "Eksperiment zavershen.") + "\n" + sb);
    }

    private static string FormatAppIdBlobFilterTable(
        AppIdFilterExperimentRow ascii,
        AppIdFilterExperimentRow unicodeFwpm,
        AppIdFilterExperimentRow unicodeRuntimeExact,
        AppIdFilterExperimentRow unicodeManualLower,
        AppIdFilterExperimentRow unicodeProductionNormalized)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("| Case | APP_ID source | text | bytes | delta | runtimeAppId |");
        sb.AppendLine("|------|---------------|------|-------|-------|--------------|");
        sb.AppendLine(FormatAppIdBlobFilterRow(ascii));
        sb.AppendLine(FormatAppIdBlobFilterRow(unicodeFwpm));
        sb.AppendLine(FormatAppIdBlobFilterRow(unicodeRuntimeExact));
        sb.AppendLine(FormatAppIdBlobFilterRow(unicodeManualLower));
        sb.AppendLine(FormatAppIdBlobFilterRow(unicodeProductionNormalized));
        return sb.ToString();
    }

    private static string FormatAppIdBlobFilterRow(AppIdFilterExperimentRow row)
        => "| " + row.CaseLabel
           + " | " + EscapeMd(row.AppIdSource)
           + " | " + EscapeMd(row.AppIdText)
           + " | " + row.AppIdByteLength
           + " | " + row.CaptureDelta
           + " | " + EscapeMd(row.RuntimeAppId)
           + " |";

    private static string InterpretAppIdBlobFilterExperiment(
        AppIdFilterExperimentRow ascii,
        AppIdFilterExperimentRow unicodeFwpm,
        AppIdFilterExperimentRow unicodeRuntimeExact,
        AppIdFilterExperimentRow unicodeManualLower,
        AppIdFilterExperimentRow unicodeProductionNormalized,
        bool manualLowerEqualsRuntimeBlob)
    {
        if (ascii.CaptureDelta == 0)
        {
            return "interpretation: ASCII control delta=0 — invalid experiment.";
        }

        if (unicodeFwpm.CaptureDelta == 0 && unicodeRuntimeExact.CaptureDelta > 0)
        {
            string line = "interpretation: ASCII delta>0, Unicode Fwpm delta=0, Unicode runtime-exact delta>0"
                + " => PROOF: FWPM ALE_APP_ID match requires byte-exact blob; FwpmGetAppIdFromFileName0 Unicode casing != runtime ALE_APP_ID.";
            if (manualLowerEqualsRuntimeBlob && unicodeManualLower.CaptureDelta > 0)
            {
                line += " manual-lower blob equals runtime bytes and delta>0 on this probe path.";
            }
            else if (unicodeManualLower.CaptureDelta > 0)
            {
                line += " manual-lower delta>0 but blob != runtime bytes — review lower canonical construction.";
            }
            else
            {
                line += " manual-lower delta=0 — lowercase canonical alone did not match on this path.";
            }

            if (unicodeProductionNormalized.CaptureDelta > 0)
            {
                line += " production WfpAleAppIdBuilder delta>0 — use normalized blob for FWPM_CONDITION_ALE_APP_ID.";
            }

            return line;
        }

        if (unicodeRuntimeExact.CaptureDelta == 0)
        {
            return "interpretation: runtime-exact manual blob delta=0 — case mismatch alone is insufficient;"
                + " investigate other byte-level differences between Fwpm and runtime APP_ID.";
        }

        return "interpretation: Unicode Fwpm delta=" + unicodeFwpm.CaptureDelta
            + " runtime-exact delta=" + unicodeRuntimeExact.CaptureDelta
            + " manual-lower delta=" + unicodeManualLower.CaptureDelta
            + " production-normalized delta=" + unicodeProductionNormalized.CaptureDelta
            + " manualLowerEqualsRuntimeBlob=" + manualLowerEqualsRuntimeBlob + ".";
    }

    private static async Task<DiagnosticResult> RunWfpRuntimeAppIdCaptureAbc(RouterEngine engine, CancellationToken ct)
    {
        const string name = "wfp-runtime-appid-capture-abc";
        if (FindProbe() is null)
        {
            return Fail(name, Bilingual("Probe.exe missing.", "Probe.exe ne naiden."));
        }

        WfpSession? wfp = engine.Wfp;
        if (wfp is not { SessionOpen: true })
        {
            return Fail(name, Bilingual(
                "Production WFP session is not open. Connect VPN Route first.",
                "WFP session ne otkryta."));
        }

        CalloutDriverClient? driver = engine.Driver;
        if (driver is not { IsLoaded: true })
        {
            return Fail(name, Bilingual("Callout driver not loaded.", "Callout-draiver ne zagruzhen."));
        }

        string asciiExe;
        try
        {
            asciiExe = Path.GetFullPath(ProbeCopyHelper.PrepareProbeCopy(AsciiProbePublicDirectory()));
        }
        catch (Exception ex)
        {
            return Fail(name, ProbeInfrastructureFailure(ex.Message));
        }

        string[] tcpArgs =
        [
            "--tcp",
            WfpRuntimeAppIdCapture.DiagnosticRemoteHost,
            WfpRuntimeAppIdCapture.DiagnosticRemotePort.ToString(),
        ];

        var experiments = new List<(string Test, string Conditions, int Count, uint AddressUInt32)>
        {
            ("A", "protocol", 1, WfpRuntimeAppIdCapture.DiagnosticRemoteAddressUInt32),
            ("B", "protocol+port", 2, WfpRuntimeAppIdCapture.DiagnosticRemoteAddressUInt32),
            ("C", "protocol+port+address (hostLE)", 3, WfpRuntimeAppIdCapture.DiagnosticRemoteAddressUInt32HostLittleEndian),
        };

        var rows = new List<RuntimeConditionExperimentRow>();
        foreach ((string test, string conditions, int count, uint address) in experiments)
        {
            RuntimeConditionExperimentRow row = await RunRuntimeConditionExperimentAsync(
                wfp, driver, asciiExe, test, conditions, count, address, tcpArgs, ct).ConfigureAwait(false);
            rows.Add(row);
            if (row.InfrastructureError is not null)
            {
                return Fail(name, row.InfrastructureError + "\n" + FormatRuntimeConditionExperimentReport(rows));
            }
        }

        RuntimeConditionExperimentRow cRow = rows[^1];
        if (cRow.CaptureDelta == 0)
        {
            RuntimeConditionExperimentRow c2 = await RunRuntimeConditionExperimentAsync(
                wfp,
                driver,
                asciiExe,
                "C2",
                "protocol+port+address (networkOrder)",
                3,
                WfpRuntimeAppIdCapture.DiagnosticRemoteAddressUInt32NetworkOrder,
                tcpArgs,
                ct).ConfigureAwait(false);
            rows.Add(c2);
            if (c2.InfrastructureError is not null)
            {
                return Fail(name, c2.InfrastructureError + "\n" + FormatRuntimeConditionExperimentReport(rows));
            }
        }

        string report = FormatRuntimeConditionExperimentReport(rows);
        return Pass(name, Bilingual(
            "Runtime condition A/B/C regression (ASCII probe).",
            "Regressiya A/B/C.") + "\n" + report);
    }

    private sealed record RuntimeConditionExperimentRow(
        string Test,
        string Conditions,
        ulong DiagnosticFilterId,
        uint CaptureBefore,
        uint CaptureAfter,
        uint CaptureDelta,
        ulong RuntimeFilterId,
        ulong RuntimeProcessId,
        bool RuntimeAppIdPresent,
        uint RuntimeAppIdByteLength,
        string RuntimeAppIdText,
        uint RemoteAddressUInt32Used,
        string? InfrastructureError);

    private static async Task<RuntimeConditionExperimentRow> RunRuntimeConditionExperimentAsync(
        WfpSession wfp,
        CalloutDriverClient driver,
        string probeExe,
        string test,
        string conditions,
        int conditionCount,
        uint remoteAddressUInt32,
        IReadOnlyList<string> tcpArgs,
        CancellationToken ct)
    {
        if (!wfp.TryAddRuntimeAppIdCaptureFilter(conditionCount, remoteAddressUInt32, out WfpRuntimeCaptureFilterResult add)
            || !add.Success)
        {
            return new RuntimeConditionExperimentRow(
                test,
                conditions,
                add.FilterId,
                0,
                0,
                0,
                0,
                0,
                false,
                0,
                "",
                remoteAddressUInt32,
                Bilingual(
                    "Could not install diagnostic filter: " + (add.Error ?? "0x" + add.FilterAddStatus.ToString("X8")),
                    "Ne udalos ustanovit filter."));
        }

        try
        {
            if (!driver.TryResetRuntimeCapture(out string resetErr))
            {
                return new RuntimeConditionExperimentRow(
                    test, conditions, add.FilterId, 0, 0, 0, 0, 0, false, 0, "", remoteAddressUInt32,
                    Bilingual("Reset runtime capture failed: " + resetErr, resetErr));
            }

            uint captureBefore = 0;
            if (driver.TryGetStatus(out CalloutArmStatus before, out _))
            {
                captureBefore = before.RuntimeCaptureCount;
            }

            ProbeRunResult run = await RunProbe(probeExe, tcpArgs, ct).ConfigureAwait(false);
            if (!run.Launched || run.LooksLikeRuntimeLaunchFailure)
            {
                return new RuntimeConditionExperimentRow(
                    test, conditions, add.FilterId, captureBefore, 0, 0, 0, 0, false, 0, "", remoteAddressUInt32,
                    ProbeInfrastructureFailure(run.LaunchError ?? run.Output.Trim()));
            }

            await Task.Delay(400, ct).ConfigureAwait(false);

            if (!driver.TryGetStatus(out CalloutArmStatus after, out string getErr))
            {
                return new RuntimeConditionExperimentRow(
                    test, conditions, add.FilterId, captureBefore, 0, 0, 0, 0, false, 0, "", remoteAddressUInt32,
                    getErr);
            }

            uint captureAfter = after.RuntimeCaptureCount;
            return new RuntimeConditionExperimentRow(
                test,
                conditions,
                add.FilterId,
                captureBefore,
                captureAfter,
                captureAfter - captureBefore,
                after.RuntimeFilterId,
                after.RuntimeProcessId,
                after.RuntimeAppIdPresent,
                after.RuntimeAppIdByteLength,
                after.RuntimeAppId,
                remoteAddressUInt32,
                null);
        }
        finally
        {
            wfp.RemoveRuntimeAppIdCaptureFilter(add.FilterKey);
        }
    }

    private static string FormatRuntimeConditionExperimentReport(IReadOnlyList<RuntimeConditionExperimentRow> rows)
    {
        WfpRuntimeCaptureFilterSetup setup = WfpSession.RuntimeCaptureFilterSetupTemplate;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("reusedProductionWfpSession=True");
        sb.AppendLine(WfpRuntimeAppIdCapture.FormatRemoteAddressEncodingReport());
        sb.AppendLine(
            "filterSetup: layer=" + setup.LayerKey
            + " subLayer=" + setup.SubLayerKey
            + " callout=" + setup.CalloutKey
            + " actionType=0x" + setup.ActionType.ToString("X8")
            + " rawContext=0x" + setup.RawContext.ToString("X16")
            + " weight=" + setup.WeightDescription);
        sb.AppendLine(WfpRuntimeCaptureConditionDiagnostics.FormatGuidVerificationTable());
        sb.AppendLine();
        sb.AppendLine("| Test | Conditions | filterId | captureBefore | captureAfter | delta | runtimeFilterId | runtimePid | appIdPresent | appIdBytes | runtimeAppId | addressUInt32 |");
        sb.AppendLine("|------|------------|----------|---------------|--------------|-------|-----------------|------------|--------------|------------|--------------|---------------|");
        foreach (RuntimeConditionExperimentRow row in rows)
        {
            sb.AppendLine("| " + row.Test
                + " | " + EscapeMd(row.Conditions)
                + " | " + row.DiagnosticFilterId
                + " | " + row.CaptureBefore
                + " | " + row.CaptureAfter
                + " | " + row.CaptureDelta
                + " | " + row.RuntimeFilterId
                + " | " + row.RuntimeProcessId
                + " | " + row.RuntimeAppIdPresent
                + " | " + row.RuntimeAppIdByteLength
                + " | " + EscapeMd(row.RuntimeAppIdText)
                + " | 0x" + row.RemoteAddressUInt32Used.ToString("X8")
                + " |");
        }

        sb.AppendLine();
        sb.AppendLine(InterpretRuntimeConditionExperiment(rows));
        return sb.ToString();
    }

    private static string InterpretRuntimeConditionExperiment(IReadOnlyList<RuntimeConditionExperimentRow> rows)
    {
        static bool Matched(RuntimeConditionExperimentRow r) => r.CaptureDelta > 0;
        RuntimeConditionExperimentRow? a = rows.FirstOrDefault(r => r.Test == "A");
        RuntimeConditionExperimentRow? b = rows.FirstOrDefault(r => r.Test == "B");
        RuntimeConditionExperimentRow? c = rows.FirstOrDefault(r => r.Test == "C");
        RuntimeConditionExperimentRow? c2 = rows.FirstOrDefault(r => r.Test == "C2");

        if (a is null)
        {
            return "interpretation: missing row A";
        }

        if (!Matched(a))
        {
            return "interpretation: A=no match => investigate ALE_CONNECT_REDIRECT path, filter weight/precedence, callout invoke, or probe traffic (not port/address yet).";
        }

        if (b is null)
        {
            return "interpretation: A=match; missing row B";
        }

        if (!Matched(b))
        {
            return "interpretation: A=match, B=no match => likely IP_REMOTE_PORT value/representation (39547).";
        }

        if (c is null)
        {
            return "interpretation: A=match, B=match; missing row C";
        }

        if (!Matched(c))
        {
            if (c2 is not null && Matched(c2))
            {
                return "interpretation: A=match, B=match, C(hostLE)=no, C2(networkOrder)=match => IP_REMOTE_ADDRESS byte order.";
            }

            if (c2 is not null)
            {
                return "interpretation: A=match, B=match, C/C2=no match => address value or non-endian issue; see addressUInt32 column.";
            }

            return "interpretation: A=match, B=match, C=no match => likely IP_REMOTE_ADDRESS representation; C2 not run or pending.";
        }

        return "interpretation: A/B/C all match at runtime => conditions OK; next step is APP_ID path comparison (ASCII/Unicode).";
    }

    private sealed record RuntimeAppIdCaptureRow(
        string LaunchPath,
        string FwpmAppId,
        uint FwpmAppIdByteLength,
        string RuntimeAleAppId,
        uint RuntimeAleAppIdByteLength,
        ulong RuntimeFilterId,
        uint CaptureCountDelta,
        ulong RuntimeProcessId,
        bool EqOrdinal,
        bool EqOrdinalIgnoreCase,
        WfpAleAppIdDiffDetail Diff,
        string? InfrastructureError);

    private static async Task<RuntimeAppIdCaptureRow> RunRuntimeAppIdCaptureCase(
        CalloutDriverClient driver,
        string launchPath,
        string fwpmAppId,
        uint fwpmAppIdByteLength,
        IReadOnlyList<string> tcpArgs,
        CancellationToken ct)
    {
        if (!driver.TryResetRuntimeCapture(out string resetErr))
        {
            return EmptyRuntimeAppIdCaptureRow(
                launchPath, fwpmAppId, fwpmAppIdByteLength,
                Bilingual("Reset runtime capture failed: " + resetErr, resetErr));
        }

        uint countBefore = 0;
        if (driver.TryGetStatus(out CalloutArmStatus before, out _))
        {
            countBefore = before.RuntimeCaptureCount;
        }

        ProbeRunResult run = await RunProbe(launchPath, tcpArgs, ct).ConfigureAwait(false);
        if (!run.Launched || run.LooksLikeRuntimeLaunchFailure)
        {
            return EmptyRuntimeAppIdCaptureRow(
                launchPath, fwpmAppId, fwpmAppIdByteLength,
                ProbeInfrastructureFailure(run.LaunchError ?? run.Output.Trim()));
        }

        await Task.Delay(400, ct).ConfigureAwait(false);

        if (!driver.TryGetStatus(out CalloutArmStatus after, out string getErr))
        {
            return EmptyRuntimeAppIdCaptureRow(launchPath, fwpmAppId, fwpmAppIdByteLength, getErr);
        }

        return BuildRuntimeAppIdCaptureRow(
            launchPath,
            fwpmAppId,
            fwpmAppIdByteLength,
            after.RuntimeAppId,
            after.RuntimeAppIdByteLength,
            after.RuntimeFilterId,
            after.RuntimeCaptureCount - countBefore,
            after.RuntimeProcessId,
            null);
    }

    private static RuntimeAppIdCaptureRow EmptyRuntimeAppIdCaptureRow(
        string launchPath,
        string fwpmAppId,
        uint fwpmAppIdByteLength,
        string error)
        => BuildRuntimeAppIdCaptureRow(
            launchPath,
            fwpmAppId,
            fwpmAppIdByteLength,
            "",
            0,
            0,
            0,
            0,
            error);

    private static RuntimeAppIdCaptureRow BuildRuntimeAppIdCaptureRow(
        string launchPath,
        string fwpmAppId,
        uint fwpmAppIdByteLength,
        string runtimeAppId,
        uint runtimeAppIdByteLength,
        ulong runtimeFilterId,
        uint captureDelta,
        ulong runtimeProcessId,
        string? infrastructureError)
    {
        bool eqOrdinal = WfpAleAppIdComparison.EqualsOrdinal(fwpmAppId, runtimeAppId);
        bool eqIgnore = WfpAleAppIdComparison.EqualsOrdinalIgnoreCase(fwpmAppId, runtimeAppId);
        WfpAleAppIdDiffDetail diff = WfpAleAppIdComparison.Analyze(
            fwpmAppId,
            runtimeAppId,
            fwpmAppIdByteLength,
            runtimeAppIdByteLength);
        return new RuntimeAppIdCaptureRow(
            launchPath,
            fwpmAppId,
            fwpmAppIdByteLength,
            runtimeAppId,
            runtimeAppIdByteLength,
            runtimeFilterId,
            captureDelta,
            runtimeProcessId,
            eqOrdinal,
            eqIgnore,
            diff,
            infrastructureError);
    }

    private static string FormatRuntimeCaptureInstallMeta(WfpRuntimeAppIdCapture capture)
        => "reusedProductionWfpSession=True"
            + " | filterAddStatus=0x" + capture.FilterAddStatus.ToString("X8")
            + " | diagnosticFilterId=" + capture.DiagnosticFilterId;

    private static string FormatRuntimeCaptureTable(
        RuntimeAppIdCaptureRow ascii,
        RuntimeAppIdCaptureRow? unicodeLong = null)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("| Case | launchPath | fwpmAppId | runtimeAppId | bytes | filterId | delta | pid | eqOrdinal | eqOrdinalIgnoreCase |");
        sb.AppendLine("|------|------------|-----------|--------------|-------|----------|-------|-----|-----------|---------------------|");
        sb.AppendLine(FormatRuntimeCaptureRow("ASCII", ascii));
        if (unicodeLong is not null)
        {
            sb.AppendLine(FormatRuntimeCaptureRow("Unicode LONG", unicodeLong));
        }

        sb.AppendLine();
        sb.AppendLine(FormatRuntimeCaptureDiffBlock("ASCII", ascii));
        if (unicodeLong is not null)
        {
            sb.AppendLine(FormatRuntimeCaptureDiffBlock("Unicode LONG", unicodeLong));
        }

        return sb.ToString();
    }

    private static string FormatRuntimeCaptureRow(string label, RuntimeAppIdCaptureRow row)
        => "| " + label
           + " | " + EscapeMd(row.LaunchPath)
           + " | " + EscapeMd(row.FwpmAppId)
           + " | " + EscapeMd(row.RuntimeAleAppId)
           + " | " + row.RuntimeAleAppIdByteLength
           + " | " + row.RuntimeFilterId
           + " | " + row.CaptureCountDelta
           + " | " + row.RuntimeProcessId
           + " | " + row.EqOrdinal
           + " | " + row.EqOrdinalIgnoreCase
           + " |";

    private static string FormatRuntimeCaptureDiffBlock(string label, RuntimeAppIdCaptureRow row)
        => label + " diff: " + WfpAleAppIdComparison.FormatDiffReport(row.Diff);

    private static string EscapeMd(string value)
        => value.Replace("|", "\\|", StringComparison.Ordinal);

}
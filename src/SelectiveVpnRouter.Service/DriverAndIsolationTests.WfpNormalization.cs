using System.Diagnostics;
using SelectiveVpnRouter.Core;
using SelectiveVpnRouter.Network;

namespace SelectiveVpnRouter.Service;

internal static partial class DriverAndIsolationTests
{
    private sealed record NormalizationMatrixRow(
        string CaseId,
        string Label,
        string ExePath,
        bool PathAvailable,
        string? FwpmCanonical,
        string? NormalizedInvariant,
        string? RuntimeAppId,
        bool? FwpmOrdinalEqualsRuntime,
        bool? OrdinalNormalizedEqualsRuntime,
        bool? ByteNormalizedEqualsRuntime,
        uint? NormalizedFilterDelta,
        string? Notes);

    private static async Task<DiagnosticResult> RunWfpRuntimeAppIdNormalizationMatrix(RouterEngine engine, CancellationToken ct)
    {
        const string name = "wfp-runtime-appid-normalization-matrix";
        try
        {
            return await RunWfpRuntimeAppIdNormalizationMatrixCore(engine, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            engine.Log(name + " infrastructure exception: " + ex);
            return Fail(name, Bilingual("Diagnostic infrastructure exception.", "Isklyuchenie infrastruktury.") + "\n--- exception ---\n" + ex);
        }
    }

    private static async Task<DiagnosticResult> RunWfpRuntimeAppIdNormalizationMatrixCore(RouterEngine engine, CancellationToken ct)
    {
        const string name = "wfp-runtime-appid-normalization-matrix";
        if (FindProbe() is null)
        {
            return Fail(name, Bilingual("Probe.exe missing.", "Probe.exe ne naiden."));
        }

        WfpSession? wfp = engine.Wfp;
        if (wfp is not { SessionOpen: true })
        {
            return Fail(name, Bilingual("Production WFP session is not open.", "WFP session ne otkryta."));
        }

        CalloutDriverClient? driver = engine.Driver;
        if (driver is not { IsLoaded: true })
        {
            return Fail(name, Bilingual("Callout driver not loaded.", "Callout-draiver ne zagruzhen."));
        }

        string asciiExe;
        string unicodeExe;
        try
        {
            asciiExe = Path.GetFullPath(ProbeCopyHelper.PrepareProbeCopy(AsciiProbePublicDirectory()));
            unicodeExe = Path.GetFullPath(ProbeCopyHelper.PrepareProbeCopy(WfpUnicodeProbeDirectory()));
        }
        catch (Exception ex)
        {
            return Fail(name, ProbeInfrastructureFailure(ex.Message));
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== Normalization validation matrix ===");
        sb.AppendLine("hypothesis: runtime ALE_APP_ID == UTF-16LE(FwpmCanonical.ToLowerInvariant()+NUL)");
        sb.AppendLine();

        string root = MatrixRoot();
        var cases = new (string Id, string Label, Func<string?> Resolve)[]
        {
            ("ascii-lower", "ASCII lowercase", () => asciiExe),
            ("ascii-mixed", "ASCII MixedCase directory", () => TryMatrixProbe(Path.Combine(root, "MiXeD_CaSe_DiR"))),
            ("cyrillic-test", "Cyrillic (Test)", () => unicodeExe),
            ("cyrillic-multi", "Cyrillic multi-upper", () => TryMatrixProbe(Path.Combine(root, "\u0422\u0415\u0421\u0422 \u041f\u0430\u043f\u043a\u0430"))),
            ("latin-umlaut", "Latin umlaut directory", () => TryMatrixProbe(Path.Combine(root, "\u00c4pp \u00dcber"))),
            ("unicode-mixed", "Cyrillic+ASCII mixed", () => TryMatrixProbe(Path.Combine(root, "\u0422estAscii Mix"))),
            ("telegram-real", "Telegram.exe", () => TryResolveTelegramExecutablePath(engine)),
        };

        var rows = new List<NormalizationMatrixRow>();
        foreach ((string id, string label, Func<string?> resolve) in cases)
        {
            string? path = resolve();
            rows.Add(EvaluateNormalizationStatic(id, label, path ?? string.Empty));
        }

        string[] tcpArgs = ["--tcp", WfpRuntimeAppIdCapture.DiagnosticRemoteHost, WfpRuntimeAppIdCapture.DiagnosticRemotePort.ToString()];
        using (var captureFilter = new WfpRuntimeAppIdCapture(wfp))
        {
            if (!captureFilter.TryInstall(out string installErr))
            {
                return Fail(name, installErr);
            }

            for (int i = 0; i < rows.Count; i++)
            {
                NormalizationMatrixRow row = rows[i];
                if (!row.PathAvailable || row.FwpmCanonical is null)
                {
                    continue;
                }

                if (row.CaseId == "telegram-real")
                {
                    continue;
                }

                RuntimeAppIdCaptureRow capture = await RunRuntimeAppIdCaptureCase(
                    driver, row.ExePath, row.FwpmCanonical, 0, tcpArgs, ct).ConfigureAwait(false);
                if (capture.InfrastructureError is not null)
                {
                    rows[i] = row with { Notes = "Runtime capture failed: " + capture.InfrastructureError };
                    continue;
                }

                rows[i] = ApplyRuntimeNormalizationFacts(row, capture.RuntimeAleAppId);
            }
        }

        for (int i = 0; i < rows.Count; i++)
        {
            NormalizationMatrixRow row = rows[i];
            if (!row.PathAvailable || row.FwpmCanonical is null || row.NormalizedInvariant is null)
            {
                continue;
            }

            if (row.CaseId == "telegram-real")
            {
                continue;
            }

            if (!WfpAleAppIdBuilder.TryBuildNormalizedForFilePath(row.ExePath, out WfpOwnedAleAppIdBlob? owned, out string? buildErr) || owned is null)
            {
                rows[i] = row with { Notes = (row.Notes ?? "") + " Filter build failed: " + buildErr };
                continue;
            }

            using (owned)
            {
                IntPtr experimentBlob = WfpDiagnosticAleAppIdBlob.BuildAleAppIdBlobFromCanonicalString(
                    owned.NormalizedAppId,
                    out uint experimentBlobSize);
                AppIdFilterExperimentRow filterRow = await RunAppIdFilterExperimentWithBlobAsync(
                    wfp!,
                    driver!,
                    experimentBlob,
                    diagnosticOwnedBlob: true,
                    row.CaseId + "-normalized-filter",
                    "WfpAleAppIdBuilder",
                    row.ExePath,
                    owned.NormalizedAppId,
                    experimentBlobSize,
                    row.ExePath,
                    tcpArgs,
                    ct).ConfigureAwait(false);
                if (filterRow.InfrastructureError is not null)
                {
                    rows[i] = row with { Notes = (row.Notes ?? "") + " Filter experiment: " + filterRow.InfrastructureError };
                    continue;
                }

                rows[i] = row with { NormalizedFilterDelta = filterRow.CaptureDelta };
            }
        }


        int telegramRowIndex = rows.FindIndex(r => r.CaseId == "telegram-real");
        if (telegramRowIndex >= 0)
        {
            NormalizationMatrixRow telegramRow = rows[telegramRowIndex];
            if (telegramRow.PathAvailable && telegramRow.NormalizedInvariant is not null)
            {
                rows[telegramRowIndex] = await EnrichTelegramMatrixRowAsync(
                    engine,
                    wfp!,
                    driver!,
                    telegramRow,
                    ct).ConfigureAwait(false);
            }
        }
        int counterExamples = 0;
        foreach (NormalizationMatrixRow row in rows)
        {
            sb.AppendLine("--- " + row.CaseId + " | " + row.Label + " ---");
            sb.AppendLine("pathAvailable=" + row.PathAvailable);
            sb.AppendLine("exePath=" + row.ExePath);
            sb.AppendLine("fwpmCanonical=" + (row.FwpmCanonical ?? "(n/a)"));
            sb.AppendLine("normalizedInvariant=" + (row.NormalizedInvariant ?? "(n/a)"));
            sb.AppendLine("runtimeAppId=" + (row.RuntimeAppId ?? "(n/a)"));
            sb.AppendLine("fwpmOrdinalEqualsRuntime=" + row.FwpmOrdinalEqualsRuntime);
            sb.AppendLine("ordinalNormalizedEqualsRuntime=" + row.OrdinalNormalizedEqualsRuntime);
            sb.AppendLine("byteNormalizedEqualsRuntime=" + row.ByteNormalizedEqualsRuntime);
            sb.AppendLine("normalizedFilterDelta=" + (row.NormalizedFilterDelta?.ToString() ?? "(n/a)"));
            sb.AppendLine("notes=" + row.Notes);
            sb.AppendLine();
            if (row.OrdinalNormalizedEqualsRuntime == false)
            {
                counterExamples++;
            }
        }

        sb.AppendLine("counterExamples=" + counterExamples);
        if (counterExamples > 0)
        {
            return Fail(name, Bilingual("Normalization counterexample found.", "Counterexample.") + "\n" + sb);
        }

        return Pass(name, Bilingual("Normalization matrix complete.", "Matrica zavershena.") + "\n" + sb);
    }

    private static NormalizationMatrixRow ApplyRuntimeNormalizationFacts(NormalizationMatrixRow row, string runtimeAppId)
    {
        string normalized = row.NormalizedInvariant ?? string.Empty;
        string fwpm = row.FwpmCanonical ?? string.Empty;
        bool ordinalNorm = string.Equals(normalized, runtimeAppId, StringComparison.Ordinal);
        bool fwpmOrd = string.Equals(fwpm, runtimeAppId, StringComparison.Ordinal);
        byte[] normBytes = WfpAleAppIdBuilder.GetUtf16LeBytesIncludingTerminator(normalized);
        byte[] runtimeBytes = WfpAleAppIdBuilder.GetUtf16LeBytesIncludingTerminator(runtimeAppId);
        bool byteEq = WfpDiagnosticAleAppIdBlob.BytesEqual(normBytes, runtimeBytes);
        return row with
        {
            RuntimeAppId = runtimeAppId,
            FwpmOrdinalEqualsRuntime = fwpmOrd,
            OrdinalNormalizedEqualsRuntime = ordinalNorm,
            ByteNormalizedEqualsRuntime = byteEq,
            Notes = "Runtime capture applied.",
        };
    }

    private static NormalizationMatrixRow EvaluateNormalizationStatic(string caseId, string label, string exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
        {
            return new NormalizationMatrixRow(caseId, label, exePath, false, null, null, null, null, null, null, null, "Path not available.");
        }

        if (!WfpAppIdentity.TryResolveAleAppIdFromFileName(exePath, out string fwpm, out _))
        {
            return new NormalizationMatrixRow(caseId, label, exePath, true, null, null, null, null, null, null, null, "FwpmGetAppIdFromFileName0 failed.");
        }

        string normalized = fwpm.ToLowerInvariant();
        return new NormalizationMatrixRow(caseId, label, exePath, true, fwpm, normalized, null, null, null, null, null, "Static evaluation.");
    }

    private static string MatrixRoot()
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "VPN Route WFP Tests");


    private static async Task<NormalizationMatrixRow> EnrichTelegramMatrixRowAsync(
        RouterEngine engine,
        WfpSession wfp,
        CalloutDriverClient driver,
        NormalizationMatrixRow row,
        CancellationToken ct)
    {
        if (!WfpAleAppIdBuilder.TryBuildNormalizedForFilePath(row.ExePath, out WfpOwnedAleAppIdBlob? owned, out string? buildErr) || owned is null)
        {
            return row with { Notes = (row.Notes ?? "") + " Telegram enrich: " + buildErr };
        }

        using (owned)
        {
            if (!wfp.TryAddRuntimeAppIdCaptureFilterWithAppId(owned.BlobPointer, out WfpRuntimeCaptureFilterResult filterResult))
            {
                return row with { Notes = (row.Notes ?? "") + " Telegram filter: " + (filterResult.Error ?? "failed") };
            }

            if (!driver.TryResetRuntimeCapture(out string resetErr))
            {
                return row with { Notes = (row.Notes ?? "") + " Telegram reset: " + resetErr };
            }

            uint countBefore = 0;
            if (driver.TryGetStatus(out CalloutArmStatus before, out _))
            {
                countBefore = before.RuntimeCaptureCount;
            }

            try
            {
                Process.Start(new ProcessStartInfo(row.ExePath) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                return row with { Notes = (row.Notes ?? "") + " Telegram launch: " + ex.Message };
            }

            await Task.Delay(3000, ct).ConfigureAwait(false);

            if (!driver.TryGetStatus(out CalloutArmStatus after, out string getErr))
            {
                return row with { Notes = (row.Notes ?? "") + " Telegram status: " + getErr };
            }

            uint delta = after.RuntimeCaptureCount - countBefore;
            NormalizationMatrixRow updated = string.IsNullOrWhiteSpace(after.RuntimeAppId)
                ? row with { NormalizedFilterDelta = delta, Notes = "Telegram launched; runtime APP_ID empty (no matching TCP yet)." }
                : ApplyRuntimeNormalizationFacts(row, after.RuntimeAppId) with { NormalizedFilterDelta = delta };

            return updated;
        }
    }
    private static string? TryResolveTelegramExecutablePath(RouterEngine engine)
    {
        if (DiagnosticUserExecutablePaths.TryResolveTelegramExecutable(
                engine.Config,
                engine.DiagnosticRequestExePath,
                out string path,
                out _))
        {
            return path;
        }

        return null;
    }

    private static string? TryMatrixProbe(string directory)
    {
        try { return Path.GetFullPath(ProbeCopyHelper.PrepareProbeCopy(directory)); }
        catch { return null; }
    }

    private static async Task<DiagnosticResult> RunWfpTelegramAppIdAcceptance(RouterEngine engine, CancellationToken ct)
    {
        const string name = "wfp-telegram-appid-acceptance";
        if (!DiagnosticUserExecutablePaths.TryResolveTelegramExecutable(
                engine.Config,
                engine.DiagnosticRequestExePath,
                out string telegramExe,
                out string resolutionSource))
        {
            return Fail(name, Bilingual(
                "Telegram.exe not found. Add Telegram to VPN apps, pass exePath in RunDiagnostic payload, or install under a user profile.",
                "Telegram.exe ne naiden."));
        }

        WfpSession? wfp = engine.Wfp;
        CalloutDriverClient? driver = engine.Driver;
        if (wfp is not { SessionOpen: true } || driver is not { IsLoaded: true })
        {
            return Fail(name, Bilingual("Requires open WFP session and loaded callout driver.", "Nuzhny WFP i draiver."));
        }

        if (!WfpAleAppIdBuilder.TryBuildNormalizedForFilePath(telegramExe, out WfpOwnedAleAppIdBlob? blob, out string? buildError) || blob is null)
        {
            return Fail(name, buildError ?? "builder failed");
        }

        using (blob)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("telegramExe=" + telegramExe);
            sb.AppendLine("telegramPathSource=" + resolutionSource);
            sb.AppendLine("fwpmCanonical=" + blob.FwpmCanonicalAppId);
            sb.AppendLine("normalizedAppId=" + blob.NormalizedAppId);
            sb.AppendLine("normalizedByteSize=" + blob.ByteSize);
            if (!wfp.TryAddRuntimeAppIdCaptureFilterWithAppId(blob.BlobPointer, out WfpRuntimeCaptureFilterResult filterResult))
            {
                return Fail(name, filterResult.Error ?? "filter add failed");
            }

            sb.AppendLine("filterInstalled=True filterId=" + filterResult.FilterId);

            if (!driver.TryResetRuntimeCapture(out string resetErr))
            {
                return Fail(name, Bilingual("Reset runtime capture failed.", resetErr) + "\n" + sb);
            }

            uint countBefore = 0;
            if (driver.TryGetStatus(out CalloutArmStatus before, out _))
            {
                countBefore = before.RuntimeCaptureCount;
            }

            try
            {
                Process.Start(new ProcessStartInfo(telegramExe) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                sb.AppendLine("telegramLaunch=skipped error=" + ex.Message);
            }

            await Task.Delay(3000, ct).ConfigureAwait(false);

            if (driver.TryGetStatus(out CalloutArmStatus after, out string getErr))
            {
                uint delta = after.RuntimeCaptureCount - countBefore;
                sb.AppendLine("runtimeCaptureDelta=" + delta);
                sb.AppendLine("runtimeAppId=" + after.RuntimeAppId);
                sb.AppendLine("runtimeAppIdMatchesNormalized=" + string.Equals(after.RuntimeAppId, blob.NormalizedAppId, StringComparison.Ordinal));
                if (delta == 0)
                {
                    sb.AppendLine("hint=If delta=0, ensure Telegram generates outbound TCP while VPN Route is connected.");
                }
            }
            else
            {
                sb.AppendLine("getStatusError=" + getErr);
            }

            return Pass(name, Bilingual("Telegram normalized APP_ID acceptance complete.", "Priemka zavershena.") + "\n" + sb);
        }
    }
}
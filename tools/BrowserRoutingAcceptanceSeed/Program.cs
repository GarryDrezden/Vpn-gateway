using SelectiveVpnRouter.Core.BrowserRouting;
using VpnRoute.BrowserRoutingAcceptanceSeed;

// Acceptance-only CLI: mutates %ProgramData%\SelectiveVpnRouter\browser-routing-state.json
// via production BrowserRoutingStateStore while the Service process is stopped.
// Not exposed over IPC and not part of the runtime contract.

if (args.Length is 0)
{
    PrintUsage();
    return 2;
}

return args[0].ToLowerInvariant() switch
{
    "seed" => RunSeed(args),
    "restore-files" => RunRestoreFiles(args),
    _ => PrintUsage()
};

static int PrintUsage()
{
    Console.Error.WriteLine("Usage:");
    Console.Error.WriteLine("  seed --expected-revision <n>");
    Console.Error.WriteLine("  restore-files --from <backup-directory>");
    return 2;
}

static int RunSeed(string[] args)
{
    long expectedRevision = 0;
    for (var i = 1; i < args.Length; i++)
    {
        if (args[i] == "--expected-revision" && i + 1 < args.Length &&
            long.TryParse(args[++i], out var parsed) && parsed >= 0)
        {
            expectedRevision = parsed;
        }
        else
        {
            Console.Error.WriteLine("Unknown or invalid seed argument: " + args[i]);
            return 2;
        }
    }

    var store = new BrowserRoutingStateStore(BrowserRoutingStateStore.DefaultPath);
    var status = store.Load();
    if (status != BrowserRoutingStoreStatus.Available)
    {
        Console.Error.WriteLine("store unavailable: " + store.UnavailableReason);
        return 3;
    }

    var current = store.Current!;
    if (current.Revision != expectedRevision)
    {
        Console.Error.WriteLine(
            $"expected revision {expectedRevision}, found {current.Revision} (generation {current.StateGeneration}).");
        return 4;
    }

    var rules = AcceptanceSeedRules.Create();
    var issues = BrowserRoutingValidator.ValidateState(
        new BrowserRoutingState(current.StateGeneration, current.Revision + 1, BrowserRoutingContract.RouteDirect, rules));
    if (issues.Count > 0)
    {
        Console.Error.WriteLine("seed rules failed validation:");
        foreach (var issue in issues)
            Console.Error.WriteLine("  " + issue.Code + " " + issue.Path);
        return 5;
    }

    try
    {
        var next = store.Update(expectedRevision, BrowserRoutingContract.RouteDirect, rules);
        Console.WriteLine(
            "OK generation=" + next.StateGeneration +
            " revision=" + next.Revision +
            " ruleCount=" + next.RuleCount +
            " defaultRoute=" + next.State.DefaultRoute);
        return 0;
    }
    catch (BrowserRoutingConcurrencyException ex)
    {
        Console.Error.WriteLine("concurrency: current revision is " + ex.CurrentRevision);
        return 6;
    }
    catch (BrowserRoutingValidationException ex)
    {
        Console.Error.WriteLine("validation: " + string.Join("; ", ex.Issues.Select(i => i.Code + "@" + i.Path)));
        return 5;
    }
    catch (BrowserRoutingPersistenceException)
    {
        Console.Error.WriteLine("persistence failed");
        return 7;
    }
}

static int RunRestoreFiles(string[] args)
{
    string? from = null;
    for (var i = 1; i < args.Length; i++)
    {
        if (args[i] == "--from" && i + 1 < args.Length)
            from = args[++i];
        else
        {
            Console.Error.WriteLine("Unknown restore-files argument: " + args[i]);
            return 2;
        }
    }

    if (string.IsNullOrWhiteSpace(from) || !Directory.Exists(from))
    {
        Console.Error.WriteLine("restore-files requires --from <existing backup directory>");
        return 2;
    }

    var dataDir = Path.GetDirectoryName(BrowserRoutingStateStore.DefaultPath)!;
    Directory.CreateDirectory(dataDir);
    var primaryName = Path.GetFileName(BrowserRoutingStateStore.DefaultPath);
    var bakName = Path.GetFileNameWithoutExtension(primaryName) + ".bak.json";

    var restored = false;
    foreach (var name in new[] { primaryName, bakName })
    {
        var source = Path.Combine(from, name);
        if (!File.Exists(source))
            continue;
        File.Copy(source, Path.Combine(dataDir, name), overwrite: true);
        restored = true;
    }

    if (!restored)
    {
        Console.Error.WriteLine("no state files found in backup directory: " + from);
        return 8;
    }

    Console.WriteLine("OK restored from " + Path.GetFullPath(from));
    return 0;
}

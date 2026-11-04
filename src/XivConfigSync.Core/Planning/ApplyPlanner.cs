using XivConfigSync.Core.Inventory;
using XivConfigSync.Core.Model;
using XivConfigSync.Core.Rules;

namespace XivConfigSync.Core.Planning;

/// <summary>What the planner proposes for one file.</summary>
public enum PlanAction
{
    /// <summary>Applied; pre-selected.</summary>
    Apply,

    /// <summary>Offered; applied only when the user selects it.</summary>
    Offer,

    /// <summary>Both sides changed; both versions are kept until the user decides.</summary>
    Conflict,

    /// <summary>Nothing to do.</summary>
    Skip,
}

/// <summary>Why the planner chose an action; a key into the string resources.</summary>
public static class PlanReason
{
    public const string UpToDate = "Reason_UpToDate";
    public const string SourceNewer = "Reason_SourceNewer";
    public const string SourceOutdated = "Reason_SourceOutdated";
    public const string LocalChangedOnly = "Reason_LocalChangedOnly";
    public const string BothChanged = "Reason_BothChanged";
    public const string NoCommonState = "Reason_NoCommonState";
    public const string MissingLocally = "Reason_MissingLocally";
    public const string NeverRule = "Reason_NeverRule";
    public const string OnRequestRule = "Reason_OnRequestRule";
    public const string Restore = "Reason_Restore";
}

/// <summary>One file in an apply plan.</summary>
public sealed class PlanItem
{
    public required string Root { get; init; }

    public required string Path { get; init; }

    public required string SourceHash { get; init; }

    public required long SourceSize { get; init; }

    public string? LocalHash { get; init; }

    public string? BaseHash { get; init; }

    public required ApplyRule Rule { get; init; }

    public required MatrixEntry Matrix { get; init; }

    public required PlanAction Action { get; init; }

    public required string Reason { get; init; }

    public IReadOnlyList<string> Flags { get; init; } = [];

    /// <summary>Whether the user selected the file; starts true for <see cref="PlanAction.Apply"/>.</summary>
    public bool Selected { get; set; }

    /// <summary>Whether the file can be selected at all.</summary>
    public bool Selectable => Action != PlanAction.Skip;
}

/// <summary>The plan for applying one revision.</summary>
public sealed class ApplyPlan
{
    public required string SourceDeviceId { get; init; }

    public required string SourceRevisionId { get; init; }

    public required bool IsOwnDevice { get; init; }

    public List<PlanItem> Items { get; } = [];

    /// <summary>Reasons that refuse the whole apply; resource keys.</summary>
    public List<string> Refusals { get; } = [];

    /// <summary>Warnings that do not refuse it; resource keys.</summary>
    public List<string> Warnings { get; } = [];

    /// <summary>Local files the source does not have; never deleted (FR-044).</summary>
    public List<string> NotInSource { get; } = [];

    public IEnumerable<PlanItem> Selected => Items.Where(item => item.Selected && item.Selectable);
}

/// <summary>
/// Decides per file between apply, offer, conflict and skip from the source's hash, the local
/// hash and what both sides are known to have shared (architecture, "Apply"). No write times are used.
/// </summary>
public static class ApplyPlanner
{
    /// <summary>Builds a plan.</summary>
    /// <param name="source">The revision to apply.</param>
    /// <param name="local">A fresh scan of this device.</param>
    /// <param name="baseTable">What this device last shared with the source device.</param>
    /// <param name="sourceHistory">Per <c>root/path</c>, every hash the file had in the source device's older revisions.</param>
    /// <param name="localHistory">Per <c>root/path</c>, every hash the file had in this device's own revisions.</param>
    /// <param name="rules">The effective rules.</param>
    /// <param name="ownDeviceId">This device.</param>
    /// <param name="localGameVersion">The game version on this device, if known.</param>
    public static ApplyPlan Plan(
        RevisionManifest source,
        ScanResult local,
        BaseTable baseTable,
        IReadOnlyDictionary<string, HashSet<string>> sourceHistory,
        IReadOnlyDictionary<string, HashSet<string>> localHistory,
        RuleSet rules,
        string ownDeviceId,
        string? localGameVersion)
    {
        var isOwn = source.DeviceId == ownDeviceId;
        var plan = new ApplyPlan { SourceDeviceId = source.DeviceId, SourceRevisionId = source.RevisionId, IsOwnDevice = isOwn };

        if (source.GameVersion is not null && localGameVersion is not null && source.GameVersion != localGameVersion)
        {
            plan.Refusals.Add("Refuse_GameVersion");
        }
        else if (source.GameVersion is null || localGameVersion is null)
        {
            plan.Warnings.Add("Warn_GameVersionUnknown");
        }

        foreach (var (root, file) in source.AllFiles())
        {
            var localFile = local.Find(root, file.Path);
            var key = BaseTable.Key(root, file.Path);
            var item = Decide(
                root,
                file,
                localFile?.Hash,
                baseTable.Get(root, file.Path),
                sourceHistory.TryGetValue(key, out var sh) ? sh : null,
                localHistory.TryGetValue(key, out var lh) ? lh : null,
                rules,
                isOwn);
            plan.Items.Add(item);
        }

        var inSource = source.AllFiles().Select(f => BaseTable.Key(f.Root, f.File.Path)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        plan.NotInSource.AddRange(local.Files
            .Select(f => BaseTable.Key(f.Root, f.Path))
            .Where(k => !inSource.Contains(k))
            .Order(StringComparer.OrdinalIgnoreCase));

        return plan;
    }

    private static PlanItem Decide(
        string root,
        FileEntry file,
        string? localHash,
        string? baseHash,
        HashSet<string>? sourceHistory,
        HashSet<string>? localHistory,
        RuleSet rules,
        bool isOwn)
    {
        var matrix = FileMatrix.Lookup(root, file.Path);
        var rule = rules.RuleFor(root, file.Path);

        PlanAction action;
        string reason;
        if (localHash == file.Hash)
        {
            (action, reason) = (PlanAction.Skip, PlanReason.UpToDate);
        }
        else if (isOwn)
        {
            // Restoring an older state of this device: every differing file is offered and pre-selected.
            (action, reason) = (PlanAction.Apply, PlanReason.Restore);
        }
        else if (rule == ApplyRule.Never)
        {
            (action, reason) = (PlanAction.Skip, PlanReason.NeverRule);
        }
        else if (localHash is null)
        {
            (action, reason) = (rule == ApplyRule.Always ? PlanAction.Apply : PlanAction.Offer, PlanReason.MissingLocally);
        }
        else if (baseHash == file.Hash || (localHistory?.Contains(file.Hash) ?? false))
        {
            // The source still has a state this device already had or shared: the local change is
            // newer. Never applied on its own, but offered, so the user can still take any file.
            (action, reason) = (PlanAction.Offer, baseHash == file.Hash ? PlanReason.LocalChangedOnly : PlanReason.SourceOutdated);
        }
        else if (baseHash == localHash || (sourceHistory?.Contains(localHash) ?? false))
        {
            // The local file is a state the source had before: the source is newer.
            (action, reason) = rule == ApplyRule.Always
                ? (PlanAction.Apply, PlanReason.SourceNewer)
                : (PlanAction.Offer, PlanReason.OnRequestRule);
        }
        else
        {
            (action, reason) = (PlanAction.Conflict, baseHash is null ? PlanReason.NoCommonState : PlanReason.BothChanged);
        }

        return new PlanItem
        {
            Root = root,
            Path = file.Path,
            SourceHash = file.Hash,
            SourceSize = file.Size,
            LocalHash = localHash,
            BaseHash = baseHash,
            Rule = rule,
            Matrix = matrix,
            Action = action,
            Reason = reason,
            Flags = file.Flags,
            Selected = action == PlanAction.Apply,
        };
    }
}

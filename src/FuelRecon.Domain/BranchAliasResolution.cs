namespace FuelRecon.Domain;

/// <summary>
/// Canonical branch master record used as the target for source-file aliases.
/// </summary>
public sealed record BranchMaster
{
    public BranchMaster(CanonicalBranchId id, string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException("Branch display name cannot be empty.", nameof(displayName));
        }

        Id = id;
        DisplayName = displayName.Trim();
    }

    public CanonicalBranchId Id { get; }

    public string DisplayName { get; }
}

/// <summary>
/// Raw branch alias mapped to an existing canonical branch ID.
/// </summary>
public sealed record BranchAlias
{
    public BranchAlias(string alias, CanonicalBranchId branchId)
    {
        if (string.IsNullOrWhiteSpace(alias))
        {
            throw new ArgumentException("Branch alias cannot be empty.", nameof(alias));
        }

        Alias = alias.Trim();
        BranchId = branchId;
        NormalisedAliasKey = BranchAliasKey.Normalise(Alias);

        if (NormalisedAliasKey.Length == 0)
        {
            throw new ArgumentException("Branch alias must contain at least one searchable character.", nameof(alias));
        }
    }

    public string Alias { get; }

    public CanonicalBranchId BranchId { get; }

    public string NormalisedAliasKey { get; }
}

/// <summary>
/// Result of branch alias resolution preserving raw source text and a machine-readable reason code on failure.
/// </summary>
public sealed record BranchAliasResolutionResult
{
    private BranchAliasResolutionResult(
        string rawValue,
        CanonicalBranchId? branchId,
        bool success,
        string? reasonCode)
    {
        RawValue = rawValue;
        BranchId = branchId;
        Success = success;
        ReasonCode = reasonCode;
    }

    public string RawValue { get; }

    public CanonicalBranchId? BranchId { get; }

    public bool Success { get; }

    public string? ReasonCode { get; }

    public static BranchAliasResolutionResult Resolved(string rawValue, CanonicalBranchId branchId) =>
        new(rawValue, branchId, success: true, reasonCode: null);

    public static BranchAliasResolutionResult Failed(string rawValue, string reasonCode) =>
        new(rawValue, branchId: null, success: false, reasonCode);
}

/// <summary>
/// Deterministic in-memory branch alias resolver. Persistence and settings UI are intentionally out of scope.
/// </summary>
public sealed class BranchAliasResolver
{
    public const string BranchAliasNotFoundReasonCode = "BranchAliasNotFound";

    public const string InvalidBranchAliasReasonCode = "InvalidBranchAlias";

    private readonly IReadOnlyDictionary<string, BranchMaster> branchesById;

    private readonly IReadOnlyDictionary<string, BranchAlias> aliasesByKey;

    public BranchAliasResolver(IEnumerable<BranchMaster> branches, IEnumerable<BranchAlias> aliases)
    {
        if (branches is null)
        {
            throw new ArgumentNullException(nameof(branches));
        }

        if (aliases is null)
        {
            throw new ArgumentNullException(nameof(aliases));
        }

        branchesById = branches.ToDictionary(
            branch => branch.Id.Value,
            branch => branch,
            StringComparer.OrdinalIgnoreCase);

        aliasesByKey = BuildAliasIndex(aliases);
    }

    public BranchAliasResolutionResult Resolve(string? rawValue)
    {
        var preservedRawValue = rawValue ?? string.Empty;

        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return BranchAliasResolutionResult.Failed(preservedRawValue, BranchAliasNotFoundReasonCode);
        }

        var aliasKey = BranchAliasKey.Normalise(rawValue);

        if (aliasKey.Length == 0 || !aliasesByKey.TryGetValue(aliasKey, out var alias))
        {
            return BranchAliasResolutionResult.Failed(preservedRawValue, BranchAliasNotFoundReasonCode);
        }

        if (!branchesById.ContainsKey(alias.BranchId.Value))
        {
            return BranchAliasResolutionResult.Failed(preservedRawValue, InvalidBranchAliasReasonCode);
        }

        return BranchAliasResolutionResult.Resolved(preservedRawValue, alias.BranchId);
    }

    public IReadOnlyCollection<BranchMaster> Branches => branchesById.Values.ToArray();

    public IReadOnlyCollection<BranchAlias> Aliases => aliasesByKey.Values.ToArray();

    private IReadOnlyDictionary<string, BranchAlias> BuildAliasIndex(IEnumerable<BranchAlias> aliases)
    {
        var index = new Dictionary<string, BranchAlias>(StringComparer.Ordinal);

        foreach (var alias in aliases)
        {
            if (!branchesById.ContainsKey(alias.BranchId.Value))
            {
                throw new ArgumentException(
                    $"Branch alias '{alias.Alias}' points to missing canonical branch ID '{alias.BranchId}'.",
                    nameof(aliases));
            }

            if (index.TryGetValue(alias.NormalisedAliasKey, out var existingAlias)
                && !string.Equals(existingAlias.BranchId.Value, alias.BranchId.Value, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    $"Branch alias '{alias.Alias}' conflicts with an existing alias for branch '{existingAlias.BranchId}'.",
                    nameof(aliases));
            }

            index[alias.NormalisedAliasKey] = alias;
        }

        return index;
    }
}

internal static class BranchAliasKey
{
    internal static string Normalise(string value) =>
        new(value
            .Where(char.IsLetterOrDigit)
            .Select(char.ToUpperInvariant)
            .ToArray());
}

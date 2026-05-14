namespace FuelRecon.Domain;

/// <summary>
/// Discrete confidence tier for a match or candidate (for example RA vs rego fallback).
/// </summary>
public enum ConfidenceBucket
{
    Unknown = 0,
    Low = 1,
    Medium = 2,
    High = 3,
}

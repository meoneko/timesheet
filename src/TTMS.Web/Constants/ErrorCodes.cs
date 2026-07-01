namespace TTMS.Web.Constants;

/// <summary>
/// Central registry of error codes returned by ServiceResult.ErrorCode.
/// Keeps error identification consistent across all services and the controller layer.
/// </summary>
public static class ErrorCodes
{
    // General
    public const string NotFound = "NotFound";
    public const string Forbidden = "Forbidden";
    public const string Required = "Required";
    public const string NoUser = "NoUser";

    // Validation
    public const string Duplicate = "Duplicate";
    public const string DuplicateCode = "DuplicateCode";
    public const string DuplicateName = "DuplicateName";
    public const string InvalidDuration = "InvalidDuration";
    public const string ValidationError = "ValidationError";

    // Project
    public const string ProjectNotActive = "ProjectNotActive";
    public const string LastOwner = "LastOwner";

    // Concurrency
    public const string ConcurrencyConflict = "ConcurrencyConflict";
}
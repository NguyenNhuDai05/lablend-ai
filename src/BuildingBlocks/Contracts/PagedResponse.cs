namespace LabLend.Contracts;

// Technical HTTP response shape; no shared domain entities or persistence model.
public sealed record PagedResponse<T>(IReadOnlyList<T> Items, long Total, int Page, int PageSize);

public static class Roles
{
    public const string Student = "STUDENT";
    public const string LabStaff = "LAB_STAFF";
    public const string Admin = "ADMIN";
}

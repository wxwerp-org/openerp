using Mapster;
using Microsoft.AspNetCore.Http.HttpResults;

namespace OpenERP.Api.Authentication;

internal static class UserManagementEndpoints
{
    internal static void MapUserManagement(this WebApplication app)
    {
        var users = app.MapGroup("/api/users").RequireAuthorization(UserAccount.AdministratorRole);
        users.MapGet("", ListAsync).WithSummary("查询用户列表");
        users.MapGet("/{id:guid}", GetAsync).WithSummary("按 UUID 查询用户")
            .ProducesProblem(StatusCodes.Status404NotFound);
        users.MapPost("", CreateAsync).WithSummary("创建普通用户")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);
        users.MapPut("/{id:guid}", UpdateAsync).WithSummary("修改普通用户资料及用户名")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        users.MapDelete("/{id:guid}", DeleteAsync).WithSummary("删除普通用户并撤销其会话")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static async Task<Ok<UserResponse[]>> ListAsync(AccountStore store, CancellationToken ct) =>
        TypedResults.Ok((await store.ListUsersAsync(ct)).Adapt<UserResponse[]>());

    private static async Task<Results<Ok<UserResponse>, ProblemHttpResult>> GetAsync(
        Guid id, AccountStore store, CancellationToken ct)
    {
        var user = await store.FindUserAsync(id, ct);
        return user is null
            ? TypedResults.Problem("用户不存在。", statusCode: StatusCodes.Status404NotFound)
            : TypedResults.Ok(user.Adapt<UserResponse>());
    }

    private static async Task<Results<Created<UserResponse>, ValidationProblem, ProblemHttpResult>> CreateAsync(
        CreateUserRequest request, AccountStore store, CancellationToken ct)
    {
        var username = request.Username?.Trim() ?? "";
        var changes = ProfileChanges.Normalize(request.Name, request.Phone, request.Email);
        var errors = ValidateUserInput(username, changes);
        if (!AccountStore.IsValidPassword(request.Password))
            errors["password"] = ["密码长度必须为 6–20 个字符。"];
        if (errors.Count > 0)
            return TypedResults.ValidationProblem(errors);

        var result = await store.CreateUserAsync(username, request.Password!, changes, ct);
        if (result.IsErr)
            return TypedResults.Problem(result.Error.Message, statusCode: StatusCodes.Status409Conflict);

        return TypedResults.Created($"/api/users/{result.Value.Id:D}", result.Value.Adapt<UserResponse>());
    }

    private static async Task<Results<Ok<UserResponse>, ValidationProblem, ProblemHttpResult>> UpdateAsync(
        Guid id, UpdateUserRequest request, AccountStore store, CancellationToken ct)
    {
        var username = request.Username?.Trim() ?? "";
        var changes = ProfileChanges.Normalize(request.Name, request.Phone, request.Email);
        var errors = ValidateUserInput(username, changes);
        if (errors.Count > 0)
            return TypedResults.ValidationProblem(errors);

        var (result, status) = await store.UpdateUserAsync(id, username, changes, ct);
        return result.IsErr
            ? TypedResults.Problem(result.Error.Message, statusCode: status)
            : TypedResults.Ok(result.Value.Adapt<UserResponse>());
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(
        Guid id, AccountStore store, CancellationToken ct)
    {
        var (result, status) = await store.DeleteUserAsync(id, ct);
        return result.IsErr ? TypedResults.Problem(result.Error.Message, statusCode: status) : TypedResults.NoContent();
    }

    private static Dictionary<string, string[]> ValidateUserInput(string username, ProfileChanges changes)
    {
        var errors = changes.Validate();
        if (username.Length is 0 or > 20)
            errors["username"] = ["用户名必填且不得超过 20 个字符。"];
        return errors;
    }

    /// <summary>新建普通用户的资料和初始密码。</summary>
    private sealed record CreateUserRequest(string? Username, string? Password, string? Name, string? Phone, string? Email);

    /// <summary>管理员可编辑的普通用户资料。</summary>
    private sealed record UpdateUserRequest(string? Username, string? Name, string? Phone, string? Email);
}

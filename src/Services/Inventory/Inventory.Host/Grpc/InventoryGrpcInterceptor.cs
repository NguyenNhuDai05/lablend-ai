using System.Security.Cryptography;
using System.Text;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Inventory.Application;
using Inventory.Domain;

namespace Inventory.Host.Grpc;

/// <summary>
/// Applied only to InventoryAllocation (health stays anonymous): checks the shared service token (OD-03)
/// and maps use-case failures to gRPC status codes plus a "lablend-code" trailer.
/// </summary>
internal sealed class InventoryGrpcInterceptor(IConfiguration configuration) : Interceptor
{
    private readonly byte[] expected = Encoding.UTF8.GetBytes($"Bearer {configuration["Inventory:ServiceToken"]}");

    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(TRequest request, ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        var presented = Encoding.UTF8.GetBytes(context.RequestHeaders.GetValue("authorization") ?? "");
        if (!CryptographicOperations.FixedTimeEquals(presented, expected))
            throw new RpcException(new Status(StatusCode.Unauthenticated, "Missing or invalid service token."));
        try
        {
            return await continuation(request, context);
        }
        catch (InventoryException ex)
        {
            throw Failure(ex.Code switch
            {
                _ when ex.StatusCode == 400 => StatusCode.InvalidArgument,
                _ when ex.StatusCode == 404 => StatusCode.NotFound,
                InventoryException.OperationIdReusedCode => StatusCode.AlreadyExists,
                InventoryException.ConcurrencyConflictCode => StatusCode.Aborted,
                _ => StatusCode.FailedPrecondition
            }, ex.Code, ex.Message);
        }
        catch (DomainRuleException ex)
        {
            throw Failure(StatusCode.FailedPrecondition, "CONFLICT", ex.Message);
        }
    }

    private static RpcException Failure(StatusCode status, string code, string message) =>
        new(new Status(status, message), new Metadata { { "lablend-code", code } });
}

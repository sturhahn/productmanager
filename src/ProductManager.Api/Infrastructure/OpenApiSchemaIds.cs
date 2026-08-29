namespace ProductManager.Api.Infrastructure;

/// <summary>
/// Schema names for the OpenAPI document. Generic DTOs must keep their type arguments in the name
/// — otherwise every <c>PagedResponse&lt;T&gt;</c> collides on one id and generated clients lose
/// the item type.
/// </summary>
public static class OpenApiSchemaIds
{
    private const string EngineNamespace = "ProductManager.Engine";

    public static string For(Type type)
    {
        if (type.IsGenericType)
        {
            var name = type.Name[..type.Name.IndexOf('`')];
            return $"{name}Of{string.Join("And", type.GetGenericArguments().Select(For))}";
        }

        if (type.IsArray)
            return $"{For(type.GetElementType()!)}Array";

        var simpleName = type.Name.Replace("+", ".");

        // The engine contract and the domain both name a type "DistributionType" — one is the API's
        // wire vocabulary, the other the engine's. Prefixing engine types keeps both in one document
        // instead of forcing one of them out of the published contract.
        if (type.Namespace == EngineNamespace && !simpleName.StartsWith("Engine", StringComparison.Ordinal))
            return $"Engine{simpleName}";

        return simpleName;
    }
}

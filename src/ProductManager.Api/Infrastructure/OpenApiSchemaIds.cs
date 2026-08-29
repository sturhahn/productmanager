namespace ProductManager.Api.Infrastructure;

/// <summary>
/// Schema names for the OpenAPI document. Generic DTOs must keep their type arguments in the name
/// — otherwise every <c>PagedResponse&lt;T&gt;</c> collides on one id and generated clients lose
/// the item type.
/// </summary>
public static class OpenApiSchemaIds
{
    public static string For(Type type)
    {
        if (type.IsGenericType)
        {
            var name = type.Name[..type.Name.IndexOf('`')];
            return $"{name}Of{string.Join("And", type.GetGenericArguments().Select(For))}";
        }

        if (type.IsArray)
            return $"{For(type.GetElementType()!)}Array";

        return type.Name.Replace("+", ".");
    }
}

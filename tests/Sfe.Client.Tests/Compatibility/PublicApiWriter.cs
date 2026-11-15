using System.Reflection;
using System.Text;

namespace Otools.Net.Sfe.Client.Tests.Compatibility;

/// <summary>
/// The public surface of an assembly as text, one line per type and member, sorted: what a program using the package
/// may call. Types of the namespaces left out (the generated client, kept by its own checks) are not written.
/// </summary>
public static class PublicApiWriter
{
    private const BindingFlags Declared =
        BindingFlags.Public
        | BindingFlags.NonPublic
        | BindingFlags.Instance
        | BindingFlags.Static
        | BindingFlags.DeclaredOnly;

    private static readonly NullabilityInfoContext Nullability = new();

    /// <summary>
    /// The surface of <paramref name="assembly"/>, without the types of the namespaces under
    /// <paramref name="leftOut"/>.
    /// </summary>
    public static string Write(Assembly assembly, string leftOut)
    {
        var text = new StringBuilder();
        var types = assembly
            .GetExportedTypes()
            .Where(type => !(type.Namespace ?? "").StartsWith(leftOut, StringComparison.Ordinal))
            .OrderBy(type => type.FullName, StringComparer.Ordinal);
        foreach (var type in types)
        {
            text.Append(TypeLine(type)).Append('\n');
            foreach (var member in Members(type).Order(StringComparer.Ordinal))
                text.Append("  ").Append(member).Append('\n');
        }
        return text.ToString();
    }

    private static string TypeLine(Type type)
    {
        var kind = type switch
        {
            { IsEnum: true } => "enum",
            { IsInterface: true } => "interface",
            { IsValueType: true } => "struct",
            { IsAbstract: true, IsSealed: true } => "static class",
            { IsSealed: true } => "sealed class",
            { IsAbstract: true } => "abstract class",
            _ => "class",
        };
        var bases = new List<string>();
        if (type is { IsClass: true, BaseType: { } baseType } && baseType != typeof(object))
            bases.Add(Name(baseType));
        // Those it declares: the runtime's own (ISerializable of exceptions, IComparable… of enums) vary by version.
        var inherited = type.BaseType?.GetInterfaces() ?? [];
        bases.AddRange(type.GetInterfaces().Except(inherited).Select(Name).Order(StringComparer.Ordinal));
        var inherits = bases.Count == 0 ? "" : " : " + string.Join(", ", bases);
        return $"{kind} {type.FullName}{inherits}";
    }

    private static IEnumerable<string> Members(Type type)
    {
        if (type.IsEnum)
            return type.GetFields(BindingFlags.Public | BindingFlags.Static)
                .Select(field => $"{field.Name} = {Convert.ToInt64(field.GetRawConstantValue())}");
        var members = type.GetMembers(Declared).Where(IsVisible);
        return members.Select(Member).OfType<string>();
    }

    private static bool IsVisible(MemberInfo member) =>
        member switch
        {
            MethodBase method => IsCallable(method) && (method is ConstructorInfo || !method.IsSpecialName),
            PropertyInfo property => property.GetAccessors(nonPublic: false).Length > 0,
            FieldInfo field => field.IsPublic || field.IsFamily,
            EventInfo @event => @event.AddMethod?.IsPublic == true,
            _ => false,
        };

    /// <summary>Public, or protected (a program's class deriving from this one calls it).</summary>
    private static bool IsCallable(MethodBase method) =>
        method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly;

    private static string? Member(MemberInfo member) =>
        member switch
        {
            ConstructorInfo constructor => $"{Static(constructor)}ctor({Parameters(constructor)})",
            MethodInfo method =>
                $"{Static(method)}{Returned(method)} {method.Name}{Generics(method)}({Parameters(method)})",
            PropertyInfo property => Property(property),
            FieldInfo field => Field(field),
            EventInfo @event => $"event {Name(@event.EventHandlerType!)} {@event.Name}",
            _ => null,
        };

    private static string Property(PropertyInfo property)
    {
        var accessors = new List<string>();
        if (property.GetMethod is { IsPublic: true })
            accessors.Add("get;");
        if (property.SetMethod is { IsPublic: true } setter)
            accessors.Add(IsInit(setter) ? "init;" : "set;");
        var isStatic = property.GetAccessors()[0].IsStatic ? "static " : "";
        var type = Name(property.PropertyType, Nullability.Create(property));
        return $"{isStatic}{type} {property.Name} {{ {string.Join(" ", accessors)} }}";
    }

    private static string Field(FieldInfo field)
    {
        var type = Name(field.FieldType, Nullability.Create(field));
        if (field.IsLiteral)
            return $"const {type} {field.Name} = {Literal(field.GetRawConstantValue(), field.FieldType)}";
        var modifiers = (field.IsStatic ? "static " : "") + (field.IsInitOnly ? "readonly " : "");
        return $"{modifiers}{type} {field.Name}";
    }

    private static string Returned(MethodInfo method) =>
        Name(method.ReturnType, Nullability.Create(method.ReturnParameter));

    private static string Parameters(MethodBase method) => string.Join(", ", method.GetParameters().Select(Parameter));

    private static string Parameter(ParameterInfo parameter)
    {
        var type = parameter.ParameterType;
        var modifier = parameter switch
        {
            { IsOut: true } => "out ",
            _ when type.IsByRef => "ref ",
            _ when parameter.IsDefined(typeof(ParamArrayAttribute)) => "params ",
            _ => "",
        };
        var info = Nullability.Create(parameter);
        var name = type.IsByRef ? Name(type.GetElementType()!) : Name(type, info);
        var value = parameter.HasDefaultValue ? " = " + Literal(parameter.DefaultValue, type) : "";
        return $"{modifier}{name} {parameter.Name}{value}";
    }

    private static string Literal(object? value, Type type) =>
        value switch
        {
            null => type.IsValueType ? "default" : "null",
            string text => $"\"{text}\"",
            bool flag => flag ? "true" : "false",
            _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "",
        };

    private static string Static(MethodBase method) => method.IsStatic ? "static " : "";

    private static string Generics(MethodInfo method) =>
        method.IsGenericMethodDefinition ? $"<{string.Join(", ", method.GetGenericArguments().Select(Name))}>" : "";

    private static bool IsInit(MethodInfo setter) =>
        setter.ReturnParameter.GetRequiredCustomModifiers().Any(type => type.Name == "IsExternalInit");

    /// <summary>The type's name with the <c>?</c> of its nullable references, its type arguments' too.</summary>
    private static string Name(Type type, NullabilityInfo info)
    {
        var mark = info.ReadState == NullabilityState.Nullable && !type.IsValueType ? "?" : "";
        if (type.IsArray && info.ElementType is { } element)
            return Name(type.GetElementType()!, element) + "[]" + mark;
        if (!type.IsGenericType || Nullable.GetUnderlyingType(type) is not null)
            return Name(type) + mark;
        var arguments = type.GetGenericArguments().Zip(info.GenericTypeArguments, Name);
        return $"{GenericName(type)}<{string.Join(", ", arguments)}>{mark}";
    }

    private static string Name(Type type)
    {
        if (Nullable.GetUnderlyingType(type) is { } underlying)
            return Name(underlying) + "?";
        if (type.IsArray)
            return Name(type.GetElementType()!) + "[]";
        if (type.IsGenericParameter)
            return type.Name;
        if (!type.IsGenericType)
            return GenericName(type);
        return $"{GenericName(type)}<{string.Join(", ", type.GetGenericArguments().Select(Name))}>";
    }

    /// <summary>The type's full name without its type arguments.</summary>
    private static string GenericName(Type type)
    {
        var name = type.IsGenericType ? type.Name[..type.Name.IndexOf('`')] : type.Name;
        if (type.IsNested)
            return $"{Name(type.DeclaringType!)}.{name}";
        return type.Namespace is { } space ? $"{space}.{name}" : name;
    }
}

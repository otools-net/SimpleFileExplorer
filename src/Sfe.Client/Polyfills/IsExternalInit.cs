#if NETSTANDARD2_0
namespace System.Runtime.CompilerServices;

/// <summary>
/// Lets <c>init</c> accessors compile on <c>netstandard2.0</c>, whose library lacks this marker type (it is in the box
/// from .NET 5).
/// </summary>
internal static class IsExternalInit { }
#endif

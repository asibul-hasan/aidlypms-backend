namespace AidlyPms.Api.Common;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public class AuthenticatedOnlyAttribute : Attribute
{
}

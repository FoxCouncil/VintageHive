namespace VintageHive.Processors.LocalServer;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
internal class DomainAttribute : Attribute, IEquatable<DomainAttribute>
{
    public string Domain { get; private set; }

    public bool IsWildcard => Domain?.Contains("*.") ?? false;

    public DomainAttribute(string domain)
    {
        Domain = domain;
    }

    public bool Equals(DomainAttribute? other)
    {
        return Domain == other.Domain;
    }

    public override bool Equals(object obj)
    {
        return Equals(obj as DomainAttribute);
    }

    public override int GetHashCode()
    {
        return Domain.GetHashCode();
    }
}

namespace Fineage.Net.Contract
{
    /// <summary>C0 skeleton: domain modules register via feature flags. Core stays domain-agnostic.</summary>
    public interface IDomainModule
    {
        string Name { get; }
        uint FeatureFlag { get; }
        bool Enabled { get; }
    }
}

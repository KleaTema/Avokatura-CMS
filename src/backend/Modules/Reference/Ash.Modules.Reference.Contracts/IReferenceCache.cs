namespace Ash.Modules.Reference.Contracts;

/// <summary>Controls the reference-data cache. Admin edit endpoints call <see cref="Invalidate"/> after a change.</summary>
public interface IReferenceCache
{
    void Invalidate();
}

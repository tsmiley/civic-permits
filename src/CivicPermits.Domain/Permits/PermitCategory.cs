namespace CivicPermits.Domain.Permits;

/// <summary>The broad kinds of permit the office issues.</summary>
public enum PermitCategory
{
    /// <summary>Construction, alteration or demolition of a structure.</summary>
    Building = 1,

    /// <summary>A sign on private or public property.</summary>
    Sign = 2,

    /// <summary>A public event such as a street fair or parade.</summary>
    Event = 3,

    /// <summary>A licence to vend goods or services on public property.</summary>
    Vendor = 4,
}

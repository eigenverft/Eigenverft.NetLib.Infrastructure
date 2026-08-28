namespace Eigenverft.NetLib.Infrastructure.Hosting.Configuration.CollectionOverrides
{
    /// <summary>
    /// Defines how an explicitly configured empty list or dictionary interacts with code-defined collection defaults.
    /// </summary>
    public enum EmptyCollectionBehavior
    {
        /// <summary>
        /// Treats an explicitly empty configured collection like no collection override and keeps the code-defined defaults.
        /// </summary>
        UseCodeDefaults = 0,

        /// <summary>
        /// Treats an explicitly empty configured collection as an intentional override to an empty collection.
        /// </summary>
        UseEmptyCollection = 1,
    }
}

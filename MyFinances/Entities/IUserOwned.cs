namespace MyFinances.Entities
{
    /// <summary>
    /// A row that belongs to one person. The database context filters
    /// every query by it and refuses to save one for somebody else.
    /// </summary>
    public interface IUserOwned
    {
        int UserId { get; set; }
    }
}
